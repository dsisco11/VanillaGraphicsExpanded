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

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Draws stored fields at the configured scale, reconstructing compact reduced views when needed.</summary>
internal sealed class CloudVolumeViewRenderer(CloudVolumeViewProgram program,CloudVolumeReconstructProgram reconstruct):IDisposable
{
    internal static readonly VertexLayoutDesc Layout=new([]);
    private readonly GraphicsPipelineLifetime lifetime=new();
    private CloudVolumeViewPass? integration,composition;
    private GraphicsPipeline[] active=[];
    private Action<GraphicsCommandContext>? submitPasses;
    private CloudVolumeViewTargets? targets;
    private ArrayGraphicsGeometry? geometry;
    private CloudVolumeViewSettings settings;
    private ulong revision,reconstructionRevision;
    private bool prepared,failed,disposed;
    internal string? Failure {get;private set;}
    internal long TargetBytes=>targets?.Bytes??0;
    /// <summary>Reports expensive pass preparations so stable draws can be distinguished from lifecycle refreshes.</summary>
    internal int PassPreparations=>(integration?.Preparations??0)+(composition?.Preparations??0);
    #region Public API
    /// <summary>Admits compiled view paths and procedural full-view geometry.</summary>
    internal bool Prepare(CloudVolumeViewSettings selection)
    {
        ObjectDisposedException.ThrowIf(disposed,this);selection.Validate();if(failed)return false;
        try
        {
            program.ConfigureOptions(()=>{program.SelfShadowing=selection.SelfShadowing;program.Diffuse=selection.Diffuse;
                program.Termination=selection.Stop>0;program.TraceDivisor=selection.TraceDivisor;});
            if(!program.EnsureReady())throw new InvalidOperationException("Stored cloud view executable unavailable.",program.PreparationFailure);
            if(selection.TraceDivisor!=1&&!reconstruct.EnsureReady())throw new InvalidOperationException("Stored cloud reconstruction executable unavailable.",reconstruct.PreparationFailure);
            var caps=GpuSupport.Graphics;
            if(caps.MaxDrawBuffers<6||GpuSupport.MaxColorAttachments<6||caps.MaxFragmentShaderStorageBlocks<3||caps.MaxTextureImageUnits<9||caps.MaxCombinedTextureImageUnits<9||
                caps.MaxUniformBufferBindings<=GpuBindingRegistry.Ubo.Frame||caps.MaxUniformBlockSize<240)
                throw new NotSupportedException("Stored cloud view exceeds rendering capabilities.");
            geometry??=new(Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
            if(prepared&&settings.TraceDivisor!=selection.TraceDivisor)ReleaseTargets();
            settings=selection;revision=program.ExecutableRevision;
            reconstructionRevision=selection.TraceDivisor==1?0:reconstruct.ExecutableRevision;prepared=true;return true;
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
            Check(current);
            program.Capture(fields,input,settings,scene,debugMode);
            program.Frame=scene.Camera;program.Lighting=scene.Lighting;program.Occupancy=fields.Occupancy;program.Regions=fields.Regions;
            program.Density=fields.Density;program.Direct=fields.Direct;program.DiffuseField=fields.Diffuse;
            program.MaterialDepth=scene.MaterialDepth;program.VisibilityDepth=scene.VisibilityDepth;program.LiquidDepth=scene.LiquidDepth;
            program.Aerial=scene.Aerial;program.Attenuation=scene.Attenuation;program.Shafts=scene.Shafts;
            integration??=new(program,lifetime);
            if(settings.TraceDivisor==1)
            {
                integration.Prepare(target,LiquidPipelineStates.SurfaceOutputs,true);
            }
            else
            {
                int divisor=settings.TraceDivisor;
                if(targets is not null&&(targets.Target.Width!=(target.Width+divisor-1)/divisor||
                    targets.Target.Height!=(target.Height+divisor-1)/divisor))ReleaseTargets();
                targets??=new CloudVolumeViewTargets(target.Width,target.Height,divisor);
                reconstruct.Frame=scene.Camera;
                reconstruct.Cloud=targets.Images[0];reconstruct.Bins=targets.Images[1];
                reconstruct.Metadata=targets.Images[2];reconstruct.MaterialDepth=scene.MaterialDepth;
                reconstruct.VisibilityDepth=scene.VisibilityDepth;reconstruct.LiquidDepth=scene.LiquidDepth;
                integration.Prepare(targets.Target,CloudVolumeViewTargets.Outputs,false);
                composition??=new(reconstruct,lifetime);
                composition.Prepare(target,LiquidPipelineStates.SurfaceOutputs,true);
            }
            Check(current);
            if(settings.TraceDivisor==1)
            {
                if(active.Length!=1||!ReferenceEquals(active[0],integration.Pipeline))active=[integration.Pipeline!];
            }
            else if(active.Length!=2||!ReferenceEquals(active[0],integration.Pipeline)||!ReferenceEquals(active[1],composition!.Pipeline))
                active=[integration.Pipeline!,composition!.Pipeline!];
            submitPasses??=SubmitPasses;
            if(!GraphicsCommandContext.TryRun("Cloud.Volume.View",active,true,submitPasses))
                throw new InvalidOperationException("Stored cloud view boundary rejected.");
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
        RenderingCleanup.Run(()=>{geometry?.Dispose();geometry=null;},ReleaseBorrows,ReleaseTargets,lifetime.Dispose);
    }
    /// <summary>Retires screen-sized attachments on resize without clearing retained cloud-space fields.</summary>
    internal void ReleaseTargets()
    {
        // Drop descriptors before their framebuffer owners; no cached target survives resize or reload.
        active=[];integration?.Dispose();composition?.Dispose();targets?.Dispose();targets=null;
    }
    #endregion
    #region Private
    /// <summary>Draws the selected immediate passes without allocating a new boundary callback each frame.</summary>
    private void SubmitPasses(GraphicsCommandContext commands)
    {
        integration!.Draw(commands,geometry!);
        // Framebuffer-to-sampler ordering across ordinary draws requires no compute/image barrier.
        if(settings.TraceDivisor!=1)composition!.Draw(commands,geometry!);
    }
    /// <summary>Reports preflight failure without changing native or stored resource ownership.</summary>
    private bool Reject(string reason){Failure=reason;return false;}
    /// <summary>Checks executable/context and source identities on both sides of drawing.</summary>
    private void Check(Func<bool> current)
    {
        if(!current()||!program.IsLinked||program.ExecutableRevision!=revision||program.ExecutableContext!=RenderContextRegistry.Current()||
            (settings.TraceDivisor!=1&&(!reconstruct.IsLinked||reconstruct.ExecutableRevision!=reconstructionRevision||reconstruct.ExecutableContext!=RenderContextRegistry.Current())))
            throw new InvalidOperationException("Stored cloud view certificate changed.");
    }
    /// <summary>Drops synchronous camera/scene borrows and private field bindings after each submission.</summary>
    private void ReleaseBorrows()
    {
        program.Frame=null;program.Lighting=null;program.Occupancy=null;program.Regions=null;program.Density=null;program.Direct=null;program.DiffuseField=null;
        program.MaterialDepth=null;program.VisibilityDepth=null;program.LiquidDepth=null;program.Aerial=null;program.Attenuation=null;program.Shafts=null;
        reconstruct.Frame=null;reconstruct.Cloud=null;reconstruct.Bins=null;reconstruct.Metadata=null;
        reconstruct.MaterialDepth=null;reconstruct.VisibilityDepth=null;reconstruct.LiquidDepth=null;
    }
    #endregion
}
