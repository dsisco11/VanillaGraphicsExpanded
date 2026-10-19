using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Submits the independent candidate view synchronously; it owns no native weather callback or existing cloud resources.</summary>
internal sealed class LightingVolumeRenderer(LightingVolumeViewProgram program,Func<LightingVolumeHeightProgram> createHeights):IDisposable
{
    internal static readonly VertexLayoutDesc Layout=new([]);
    private readonly GraphicsPipelineLifetime lifetime=new();
    private LightingVolumeHeightProgram? heights;
    private GraphicsPipeline? pipeline;
    private ArrayGraphicsGeometry? geometry;
    private LightingVolumeViewSettings settings;
    private ulong heightRevision,viewRevision;
    private bool prepared,failed,disposed;
    internal bool Ready { get; private set; }
    internal string? Failure { get; private set; }

    #region Public API
    /// <summary>Admits compiled candidate options and procedural geometry without registering a live renderer.</summary>
    internal bool Prepare(LightingVolumeViewSettings selection)
    {
        ObjectDisposedException.ThrowIf(disposed,this);selection.Validate();
        if(failed) return false;
        try
        {
            program.ConfigureOptions(()=>{program.Detail=selection.Detail;program.SelfShadowing=selection.SelfShadowing;
                program.Diffuse=selection.Diffuse;program.Termination=selection.Stop>0;});
            if(!program.EnsureReady()) throw new InvalidOperationException("Candidate view executable unavailable.",program.PreparationFailure);
            heights??=createHeights();
            if(!heights.EnsureReady()) throw new InvalidOperationException(heights.PreparationLog);
            var caps=GpuSupport.Graphics;
            if(caps.MaxDrawBuffers<6 || caps.MaxFragmentShaderStorageBlocks<2 || caps.MaxTextureImageUnits<9 || caps.MaxCombinedTextureImageUnits<9 ||
                caps.MaxUniformBufferBindings<=GpuBindingRegistry.Ubo.Frame || caps.MaxComputeWorkGroupInvocations<64 ||
                caps.MaxComputeWorkGroupSize[0]<64) throw new NotSupportedException("Candidate view exceeds rendering capabilities.");
            geometry??=new(Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
            settings=selection;viewRevision=program.ExecutableRevision;heightRevision=((IGpuProgram)heights).ExecutableRevision;
            prepared=true;return true;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;return false;}
    }
    /// <summary>Reduces current support and draws one full-view native OIT contribution from caller-certified resources.</summary>
    internal bool Draw(LightingVolumeStorage storage,CloudMapInput input,GpuTexture map,LightingVolumeViewScene scene,
        GpuFramebuffer target,Func<bool> current,int debugMode=0)
    {
        ObjectDisposedException.ThrowIf(disposed,this);Ready=false;
        if(!prepared||failed||!storage.Accepts(input)||!current()||scene.Camera.FrameIndex!=input.Frame ||
            scene.Camera.CalibrationGeneration!=scene.Atmosphere.CalibrationGeneration ||
            map.TextureId!=input.Storage.Map || map.Width!=input.Storage.Width || map.Height!=input.Storage.Width ||
            map.TextureTarget!=TextureTarget.Texture2D || map.InternalFormat!=PixelInternalFormat.Rgba32f) return false;
        Vector2 extent=MemoryMarshal.Read<Vector2>(scene.Camera.Bytes.Slice(384,8));
        if(extent.X!=target.Width||extent.Y!=target.Height) return false;
        try
        {
            Check(current);
            heights!.Capture(input.Storage.Width);heights.Map=map;heights.Scratch=storage.Scratch;
            Reduce();Check(current);
            program.Capture(storage.Domain!,input.Placement,storage.Encoding,settings,scene,debugMode);
            program.Frame=scene.Camera;program.Lighting=scene.Lighting;program.Scratch=storage.Scratch;
            program.Map=map;program.Direct=storage.Direct;program.DiffuseField=storage.Diffuse;
            program.MaterialDepth=scene.MaterialDepth;program.VisibilityDepth=scene.VisibilityDepth;program.LiquidDepth=scene.LiquidDepth;
            program.Aerial=scene.Aerial;program.Attenuation=scene.Attenuation;program.Shafts=scene.Shafts;
            var pass=new RenderPassDesc(target,LiquidPipelineStates.SurfaceOutputs);
            using var metadata=new RenderPassTargets(pass);
            var description=new GraphicsPipelineDesc(program.GraphicsIdentity!,Layout,metadata.Signature,
                DynamicPipelineState.Viewport,blending:LiquidPipelineStates.SurfaceBlending);
            if(pipeline is null||pipeline.Description!=description||pipeline.ExecutableRevision!=program.ExecutableRevision)
            {pipeline?.Dispose();pipeline=new GraphicsPipeline(lifetime,description,program);}
            Check(current);
            if(!GraphicsCommandContext.TryRun("Cloud.LightingVolume.View",[pipeline],true,commands=>
            {
                commands.BeginPass(pass);commands.SetPipeline(pipeline);
                commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry!,new(0,3));commands.EndPass();
            })) throw new InvalidOperationException("Candidate view graphics boundary rejected.");
            Check(current);Ready=true;return true;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
        finally {heights!.Map=null;heights.Scratch=null;ReleaseBorrows();}
    }
    /// <summary>Retires owned draw/reduction state; borrowed view program and lighting storage keep their owning lifetimes.</summary>
    internal void Reset()
    {
        Ready=prepared=failed=false;Failure=null;
        RenderingCleanup.Run(()=>{pipeline?.Dispose();pipeline=null;},()=>{heights?.Dispose();heights=null;},
            ()=>{geometry?.Dispose();geometry=null;});
        ReleaseBorrows();
    }
    /// <summary>Releases private pipelines and geometry without unregistering or disposing borrowed native objects.</summary>
    public void Dispose() {if(disposed)return;disposed=true;RenderingCleanup.Run(Reset,lifetime.Dispose);}
    #endregion
    #region Private
    /// <summary>Separates current support reduction from fragment readers with SSBO visibility barriers.</summary>
    private void Reduce()
    {
        var bindings=heights!.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Height bindings missing.");
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.LightingVolume.Heights"),EngineBoundaryResources.From(bindings),_=>
        {
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.TextureUpdateBarrierBit);
            heights.Dispatch(1);
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        })) throw new InvalidOperationException("Candidate height reduction boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud lighting volume height reduction");
    }
    /// <summary>Rejects changed input or executable certificates before publishing any successful view.</summary>
    private void Check(Func<bool> current)
    {
        if(!current()||!program.IsLinked||program.ExecutableRevision!=viewRevision||heights?.IsValid!=true||
            ((IGpuProgram)heights).ExecutableRevision!=heightRevision) throw new InvalidOperationException("Candidate view certificate changed.");
    }
    /// <summary>Releases every synchronous camera, native and atmosphere borrow after drawing or reset.</summary>
    private void ReleaseBorrows()
    {
        program.Frame=null;program.Lighting=null;program.Scratch=null;program.Map=null;program.Direct=null;program.DiffuseField=null;
        program.MaterialDepth=null;program.VisibilityDepth=null;program.LiquidDepth=null;program.Aerial=null;program.Attenuation=null;program.Shafts=null;
    }
    #endregion
}
