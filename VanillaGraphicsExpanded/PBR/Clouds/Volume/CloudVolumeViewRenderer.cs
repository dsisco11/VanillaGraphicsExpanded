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
internal sealed class CloudVolumeViewRenderer(CloudVolumeViewProgram program,CloudVolumeReconstructProgram reconstruct,CloudVolumeTemporalProgram temporal):IDisposable
{
    internal static readonly VertexLayoutDesc Layout=new([]);
    private readonly GraphicsPipelineLifetime lifetime=new();
    private CloudVolumeViewPass? integration,composition;
    private GraphicsPipeline[] active=[];
    private Action<GraphicsCommandContext>? submitPasses;
    private CloudVolumeViewTargets? targets;
    private CloudVolumeViewTargets? currentTrace;
    private readonly CloudReprojectionHistory history=new();
    private readonly CloudVolumeViewTargets?[] histories=new CloudVolumeViewTargets?[2];
    private readonly CloudVolumeViewPass?[] temporalPasses=new CloudVolumeViewPass?[2];
    private CloudBlueNoiseTexture? noise;
    private GpuTexture? historyDensity;
    private object? historyMaterial;
    private CloudVolumeDomain? historyDomain;
    private CloudVolumeViewSettings historySettings;
    private bool historyUnderwater;
    private int cursor,historyDebug;
    private ulong temporalRevision;
    private ArrayGraphicsGeometry? geometry;
    private CloudVolumeViewSettings settings;
    private ulong revision,reconstructionRevision;
    private bool prepared,failed,disposed;
    internal string? Failure {get;private set;}
    internal long TargetBytes=>(targets?.Bytes??0)+(histories[0]?.Bytes??0)+(histories[1]?.Bytes??0);
    /// <summary>Reports whether the last submitted trace could reuse a consecutive compatible view.</summary>
    internal bool HistoryCompatible {get;private set;}
    /// <summary>Exposes current trace receipts for opt-in work measurement through ordinary texture readback.</summary>
    internal DynamicTexture2D? TraceReceipts=>currentTrace?.Images[2];
    /// <summary>Exposes completed history confidence for focused temporal admission diagnostics.</summary>
    internal DynamicTexture2D? HistoryReceipts=>histories[1-cursor]?.Images[2];
    /// <summary>Borrows optional per-pass timer queries for opt-in attribution, never read on the render path.</summary>
    internal GpuTimerQuery[]? PassTimers {get;set;}
    /// <summary>Reports expensive pass preparations so stable draws can be distinguished from lifecycle refreshes.</summary>
    internal int PassPreparations=>(integration?.Preparations??0)+(composition?.Preparations??0)+(temporalPasses[0]?.Preparations??0)+(temporalPasses[1]?.Preparations??0);
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
            if(selection.TraceDivisor!=1&&!temporal.EnsureReady())throw new InvalidOperationException("Cloud temporal executable unavailable.",temporal.PreparationFailure);
            var caps=GpuSupport.Graphics;
            if(caps.MaxDrawBuffers<6||GpuSupport.MaxColorAttachments<6||caps.MaxFragmentShaderStorageBlocks<3||caps.MaxTextureImageUnits<10||caps.MaxCombinedTextureImageUnits<10||
                caps.MaxUniformBufferBindings<=GpuBindingRegistry.Ubo.Frame||caps.MaxUniformBlockSize<240)
                throw new NotSupportedException("Stored cloud view exceeds rendering capabilities.");
            geometry??=new(Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
            if(prepared&&settings.TraceDivisor!=selection.TraceDivisor)ReleaseTargets();
            settings=selection;revision=program.ExecutableRevision;
            temporalRevision=selection.TraceDivisor==1?0:temporal.ExecutableRevision;
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
                history.Invalidate();
                integration.Prepare(target,LiquidPipelineStates.SurfaceOutputs,true);
            }
            else
            {
                int divisor=settings.TraceDivisor;
                if(targets is not null&&(targets.Target.Width!=(target.Width+divisor*2-1)/(divisor*2)||
                    targets.Target.Height!=(target.Height+divisor*2-1)/(divisor*2)||
                    histories[0]!.Target.Width!=(target.Width+divisor-1)/divisor||histories[0]!.Target.Height!=(target.Height+divisor-1)/divisor))ReleaseTargets();
                targets??=new CloudVolumeViewTargets(target.Width,target.Height,divisor*2);
                histories[0]??=new CloudVolumeViewTargets(target.Width,target.Height,divisor);
                histories[1]??=new CloudVolumeViewTargets(target.Width,target.Height,divisor);
                noise??=CloudBlueNoiseTexture.Create();noise.EnsureCreated();program.BlueNoise=noise;
                bool compatible=ReferenceEquals(historyDensity,fields.Density)&&ReferenceEquals(historyMaterial,fields.MaterialIdentity)&&historyDomain is {} oldDomain
                    &&oldDomain.Width==fields.Domain.Width&&oldDomain.Bottom==fields.Domain.Bottom&&oldDomain.Band==fields.Domain.Band
                    &&historySettings==settings&&historyUnderwater==(scene.Underwater>.7f)&&historyDebug==debugMode;
                int historyWidth=histories[0]!.Target.Width,historyHeight=histories[0]!.Target.Height;
                HistoryCompatible=history.Capture(input,scene.Camera,historyWidth,historyHeight,program.Temporal,compatible,divisor);
                history.Capture(input,scene.Camera,historyWidth,historyHeight,temporal.Temporal,compatible,divisor);
                if(!HistoryCompatible)cursor=0;
                var previous=histories[1-cursor]!;var next=histories[cursor]!;
                // Invalid history is scratch: trace its full reduced grid before filtering into the
                // disjoint destination. Compatible frames use a quarter-sized physical raw target.
                currentTrace=HistoryCompatible?targets:previous;
                temporal.Frame=scene.Camera;temporal.Lighting=scene.Lighting;
                temporal.BlueNoise=noise;
                temporal.Cloud=currentTrace.Images[0];temporal.Bins=currentTrace.Images[1];temporal.Metadata=currentTrace.Images[2];
                temporal.PreviousCloud=previous.Images[0];temporal.PreviousBins=previous.Images[1];temporal.PreviousMetadata=previous.Images[2];
                temporal.MaterialDepth=scene.MaterialDepth;temporal.VisibilityDepth=scene.VisibilityDepth;temporal.LiquidDepth=scene.LiquidDepth;
                reconstruct.Frame=scene.Camera;
                reconstruct.Lighting=scene.Lighting;
                reconstruct.Cloud=next.Images[0];reconstruct.Bins=next.Images[1];
                reconstruct.Metadata=next.Images[2];reconstruct.MaterialDepth=scene.MaterialDepth;
                reconstruct.VisibilityDepth=scene.VisibilityDepth;reconstruct.LiquidDepth=scene.LiquidDepth;
                integration.Prepare(currentTrace.Target,CloudVolumeViewTargets.Outputs,false);
                temporalPasses[cursor]??=new(temporal,lifetime);
                temporalPasses[cursor]!.Prepare(next.Target,CloudVolumeViewTargets.Outputs,false);
                composition??=new(reconstruct,lifetime);
                composition.Prepare(target,LiquidPipelineStates.SurfaceOutputs,true);
            }
            Check(current);
            if(settings.TraceDivisor==1)
            {
                if(active.Length!=1||!ReferenceEquals(active[0],integration.Pipeline))active=[integration.Pipeline!];
            }
            else if(active.Length!=3||!ReferenceEquals(active[0],integration.Pipeline)||!ReferenceEquals(active[1],temporalPasses[cursor]!.Pipeline)||!ReferenceEquals(active[2],composition!.Pipeline))
                active=[integration.Pipeline!,temporalPasses[cursor]!.Pipeline!,composition!.Pipeline!];
            submitPasses??=SubmitPasses;
            if(!GraphicsCommandContext.TryRun("Cloud.Volume.View",active,true,submitPasses))
                throw new InvalidOperationException("Stored cloud view boundary rejected.");
            Check(current);
            if(settings.TraceDivisor!=1)
            {
                history.Commit(input,scene.Camera);historyDensity=fields.Density;historyMaterial=fields.MaterialIdentity;historyDomain=fields.Domain;
                historySettings=settings;historyUnderwater=scene.Underwater>.7f;historyDebug=debugMode;cursor=1-cursor;
            }
            return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {history.Invalidate();failed=true;Failure=error.Message;return false;}
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
        active=[];history.Invalidate();HistoryCompatible=false;historyDensity=null;historyMaterial=null;historyDomain=null;cursor=0;
        integration?.Dispose();composition?.Dispose();targets?.Dispose();targets=null;currentTrace=null;
        for(int i=0;i<2;i++){temporalPasses[i]?.Dispose();temporalPasses[i]=null;histories[i]?.Dispose();histories[i]=null;}
        noise?.Dispose();noise=null;
    }
    #endregion
    #region Private
    /// <summary>Draws the selected immediate passes without allocating a new boundary callback each frame.</summary>
    private void SubmitPasses(GraphicsCommandContext commands)
    {
        using(PassTimers?[0].BeginScope())integration!.Draw(commands,geometry!);
        // Framebuffer-to-sampler ordering across ordinary draws requires no compute/image barrier.
        if(settings.TraceDivisor!=1)
        {
            using(PassTimers?[1].BeginScope())temporalPasses[cursor]!.Draw(commands,geometry!);
            using(PassTimers?[2].BeginScope())composition!.Draw(commands,geometry!);
        }
    }
    /// <summary>Reports preflight failure without changing native or stored resource ownership.</summary>
    private bool Reject(string reason){history.Invalidate();Failure=reason;return false;}
    /// <summary>Checks executable/context and source identities on both sides of drawing.</summary>
    private void Check(Func<bool> current)
    {
        if(!current()||!program.IsLinked||program.ExecutableRevision!=revision||program.ExecutableContext!=RenderContextRegistry.Current()||
            (settings.TraceDivisor!=1&&(!reconstruct.IsLinked||reconstruct.ExecutableRevision!=reconstructionRevision||reconstruct.ExecutableContext!=RenderContextRegistry.Current()
                ||!temporal.IsLinked||temporal.ExecutableRevision!=temporalRevision||temporal.ExecutableContext!=RenderContextRegistry.Current())))
            throw new InvalidOperationException("Stored cloud view certificate changed.");
    }
    /// <summary>Drops synchronous camera/scene borrows and private field bindings after each submission.</summary>
    private void ReleaseBorrows()
    {
        program.Frame=null;program.Lighting=null;program.Occupancy=null;program.Regions=null;program.Density=null;program.Direct=null;program.DiffuseField=null;
        program.MaterialDepth=null;program.VisibilityDepth=null;program.LiquidDepth=null;program.Aerial=null;program.Attenuation=null;program.Shafts=null;
        reconstruct.Frame=null;reconstruct.Cloud=null;reconstruct.Bins=null;reconstruct.Metadata=null;
        reconstruct.Lighting=null;program.BlueNoise=null;
        reconstruct.MaterialDepth=null;reconstruct.VisibilityDepth=null;reconstruct.LiquidDepth=null;
        temporal.Frame=null;temporal.Lighting=null;temporal.Cloud=null;temporal.Bins=null;temporal.Metadata=null;
        temporal.PreviousCloud=null;temporal.PreviousBins=null;temporal.PreviousMetadata=null;
        temporal.BlueNoise=null;
        temporal.MaterialDepth=null;temporal.VisibilityDepth=null;temporal.LiquidDepth=null;
    }
    #endregion
}
