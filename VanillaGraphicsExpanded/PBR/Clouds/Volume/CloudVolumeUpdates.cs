using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Separates bounded update submission from nonblocking completion and drawable publication.</summary>
internal sealed class CloudVolumeUpdates(CloudVolumeStorage storage,CloudVolumeDensity density,CloudVolumeWork work,
    CloudVolumeDirect direct,CloudVolumeDiffuse diffuse,CloudVolumePublication publication):IDisposable
{
    private enum Step { Idle,Density,Lighting,Direct,DiffuseReady,Diffuse }
    private Step step;
    private GpuFence? fence;
    private CloudVolumeWorkBatch? batch;
    private (nint Handle,long Generation) context;
    private readonly Vector4[] planes=new Vector4[6];
    internal bool Pending=>step!=Step.Idle;
    #region Public API
    /// <summary>Polls with zero timeout and publishes only completed, validated working fields.</summary>
    internal void Poll()
    {
        if(step==Step.Density)
        {
            if(!density.Poll(storage))return;
            if(density.UpdateCompleted)step=storage.Ready?Step.Lighting:Step.Idle;
            return;
        }
        if(fence is null)return;
        if(context!=RenderContextRegistry.Current())throw new InvalidOperationException("Cloud completion context changed.");
        var status=fence.Poll();if(status==WaitSyncStatus.TimeoutExpired)return;
        if(status==WaitSyncStatus.WaitFailed)throw new InvalidOperationException("Cloud lighting fence failed.");
        fence.Dispose();fence=null;
        var selected=batch??throw new InvalidOperationException("Cloud completion batch missing.");
        work.VerifyStatus(selected.Fields);
        if(step==Step.Direct){step=Step.DiffuseReady;return;}
        if(step!=Step.Diffuse)throw new InvalidOperationException("Cloud completion state invalid.");
        publication.Publish(selected.Fields,selected.NewlyActive,storage.StorageBytes);
        batch=null;step=Step.Idle;
    }
    /// <summary>Submits after drawing; pending work keeps its frozen inputs while later observations coalesce.</summary>
    internal void Submit(CloudMapInput input,GpuTexture map,AtmosphereLighting lighting,VgeFrameUniformBuffer camera,
        CloudVolumeViewSettings settings,CloudVolumeAdmission admission,Func<bool> current)
    {
        if(fence is not null||density.Busy)return;
        switch(step)
        {
            case Step.Idle:
            case Step.Density:
                density.Submit(storage,input,map,admission.Encoding,.9f*lighting.Environment,current);step=Step.Density;
                break;
            case Step.Lighting:
                CloudVolumeCoordinates.Frustum(camera,CloudVolumeCoordinates.Origin(storage.Published.Domain,input),planes);
                if(!work.Select(storage,lighting,input.Frame,planes,current,settings.SelfShadowing,settings.Diffuse)||
                    !work.TryBatch(storage,out batch)||!direct.Update(storage,work,current,deferStatus:true))
                    throw new InvalidOperationException(work.Failure??direct.Failure??"Cloud direct submission failed.");
                step=Step.Direct;context=RenderContextRegistry.Current();fence=GpuFence.Insert();
                break;
            case Step.DiffuseReady:
                if(!diffuse.Update(storage,work,current,deferStatus:true))
                    throw new InvalidOperationException(diffuse.Failure??"Cloud diffuse submission failed.");
                step=Step.Diffuse;context=RenderContextRegistry.Current();fence=GpuFence.Insert();
                break;
        }
    }
    /// <summary>Revokes queued publication and retires fences without waiting for the GPU.</summary>
    public void Dispose(){fence?.Dispose();fence=null;batch=null;step=Step.Idle;}
    #endregion
}
