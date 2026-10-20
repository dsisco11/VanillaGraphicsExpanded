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

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Draws the stored volume through ordinary full-resolution graphics authority without native density access.</summary>
internal sealed class CloudVolumeViewRenderer(CloudVolumeViewProgram program):IDisposable
{
    internal static readonly VertexLayoutDesc Layout=new([]);
    private readonly GraphicsPipelineLifetime lifetime=new();
    private GraphicsPipeline? pipeline;
    private ArrayGraphicsGeometry? geometry;
    private CloudVolumeViewSettings settings;
    private ulong revision;
    private bool prepared,failed,disposed;
    internal string? Failure {get;private set;}
    #region Public API
    /// <summary>Admits compiled view paths and procedural full-view geometry.</summary>
    internal bool Prepare(CloudVolumeViewSettings selection)
    {
        ObjectDisposedException.ThrowIf(disposed,this);selection.Validate();if(failed)return false;
        try
        {
            program.ConfigureOptions(()=>{program.SelfShadowing=selection.SelfShadowing;program.Diffuse=selection.Diffuse;program.Termination=selection.Stop>0;});
            if(!program.EnsureReady())throw new InvalidOperationException("Stored cloud view executable unavailable.",program.PreparationFailure);
            var caps=GpuSupport.Graphics;
            if(caps.MaxDrawBuffers<6||caps.MaxFragmentShaderStorageBlocks<3||caps.MaxTextureImageUnits<9||caps.MaxCombinedTextureImageUnits<9||
                caps.MaxUniformBufferBindings<=GpuBindingRegistry.Ubo.Frame||caps.MaxUniformBlockSize<256)
                throw new NotSupportedException("Stored cloud view exceeds rendering capabilities.");
            geometry??=new(Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
            settings=selection;revision=program.ExecutableRevision;prepared=true;return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error)){failed=true;Failure=error.Message;return false;}
    }
    /// <summary>Validates independent publications and submits exactly one native six-attachment contribution.</summary>
    internal bool Draw(CloudVolumeStorage storage,CloudMapInput input,CloudVolumeViewScene scene,GpuFramebuffer target,Func<bool> current,int debugMode=0)
    {
        if(!storage.Ready)return Reject("Stored cloud publication unavailable.");
        var fields=storage.Published;uint stamp=fields.Revision;
        return Draw(fields.View,input,scene,target,()=>current()&&storage.Ready&&
            ReferenceEquals(fields,storage.Published)&&fields.Revision==stamp,debugMode);
    }
    /// <summary>Draws an immutable publication independently of unfinished solver storage.</summary>
    internal bool Draw(CloudVolumeViewFields fields,CloudMapInput input,CloudVolumeViewScene scene,GpuFramebuffer target,Func<bool> current,int debugMode=0)
    {
        ObjectDisposedException.ThrowIf(disposed,this);Failure=null;
        if(!prepared||failed||!current())return Reject("Stored cloud publication unavailable.");
        if(scene.Camera.FrameIndex!=input.Frame||scene.Atmosphere.CalibrationGeneration==0)return Reject("Stored cloud camera or atmosphere publication unavailable.");
        Vector2 extent=MemoryMarshal.Read<Vector2>(scene.Camera.Bytes.Slice(384,8));
        if(extent.X!=target.Width||extent.Y!=target.Height)return Reject("Stored cloud camera/OIT extent mismatch.");
        try
        {
            Check(current);program.Capture(fields,input,settings,scene,debugMode);
            program.Frame=scene.Camera;program.Lighting=scene.Lighting;program.Occupancy=fields.Occupancy;program.Regions=fields.Regions;
            program.Density=fields.Density;program.Direct=fields.Direct;program.DiffuseField=fields.Diffuse;
            program.MaterialDepth=scene.MaterialDepth;program.VisibilityDepth=scene.VisibilityDepth;program.LiquidDepth=scene.LiquidDepth;
            program.Aerial=scene.Aerial;program.Attenuation=scene.Attenuation;program.Shafts=scene.Shafts;
            var pass=new RenderPassDesc(target,LiquidPipelineStates.SurfaceOutputs);
            using var metadata=new RenderPassTargets(pass);
            var description=new GraphicsPipelineDesc(program.GraphicsIdentity!,Layout,metadata.Signature,
                DynamicPipelineState.Viewport,blending:LiquidPipelineStates.SurfaceBlending);
            if(pipeline is null||pipeline.Description!=description||pipeline.ExecutableRevision!=program.ExecutableRevision)
            {pipeline?.Dispose();pipeline=new GraphicsPipeline(lifetime,description,program);}
            Check(current);
            if(!GraphicsCommandContext.TryRun("Cloud.Volume.View",[pipeline],true,commands=>
                {commands.BeginPass(pass);commands.SetPipeline(pipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});
                    commands.Draw(geometry!,new(0,3));commands.EndPass();}))throw new InvalidOperationException("Stored cloud view boundary rejected.");
            Check(current);
            return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;return false;}
        finally{ReleaseBorrows();}
    }
    /// <summary>Releases owned pipelines and geometry while retaining external shader-library ownership.</summary>
    public void Dispose()
    {
        if(disposed)return;disposed=true;prepared=false;
        RenderingCleanup.Run(()=>{pipeline?.Dispose();pipeline=null;},()=>{geometry?.Dispose();geometry=null;},ReleaseBorrows,lifetime.Dispose);
    }
    #endregion
    #region Private
    /// <summary>Reports preflight failure without changing native or stored resource ownership.</summary>
    private bool Reject(string reason){Failure=reason;return false;}
    /// <summary>Checks executable/context and source identities on both sides of drawing.</summary>
    private void Check(Func<bool> current)
    {
        if(!current()||!program.IsLinked||program.ExecutableRevision!=revision||program.ExecutableContext!=RenderContextRegistry.Current())
            throw new InvalidOperationException("Stored cloud view certificate changed.");
    }
    /// <summary>Drops synchronous camera/scene borrows and private field bindings after each submission.</summary>
    private void ReleaseBorrows()
    {
        program.Frame=null;program.Lighting=null;program.Occupancy=null;program.Regions=null;program.Density=null;program.Direct=null;program.DiffuseField=null;
        program.MaterialDepth=null;program.VisibilityDepth=null;program.LiquidDepth=null;program.Aerial=null;program.Attenuation=null;program.Shafts=null;
    }
    #endregion
}
