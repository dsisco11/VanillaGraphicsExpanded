using System;
using System.Numerics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Separates bounded update submission from nonblocking completion and drawable publication.</summary>
internal sealed class CloudVolumeUpdates(CloudVolumeStorage storage,CloudVolumeDensity density,CloudVolumeWork work,
    CloudVolumeDirect direct,CloudVolumeDiffuse diffuse,CloudVolumePublication publication,CloudVolumeSubmission submission):IDisposable
{
    /// <summary>Separates CPU domain admission from the single dependent lighting chain.</summary>
    private enum Step { Idle,Density,Lighting,Pending }
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
        if(step!=Step.Pending)throw new InvalidOperationException("Cloud completion state invalid.");
        direct.VerifyCompletion();
        if(!diffuse.Complete(storage,work,()=>true))throw new InvalidOperationException(diffuse.Failure);
        // Lighting used the ordered GPU density writes, retaining the CPU batch certificate
        // until its solvers finished. Accept density's summary only after that certificate retires.
        if(density.Chained)density.CompletePending(storage);
        publication.Publish(selected.Fields,selected.NewlyActive,storage.StorageBytes);
        batch=null;step=Step.Idle;
    }
    /// <summary>Submits after drawing; pending work keeps its frozen inputs while later observations coalesce.</summary>
    internal void Submit(CloudMapInput input,GpuTexture map,AtmosphereLighting lighting,VgeFrameUniformBuffer camera,
        CloudVolumeViewSettings settings,CloudVolumeAdmission admission,Func<bool> current)
    {
        if(fence is not null||density.Busy)return;
        // CPU admission still ends its callback at the existing fences. The prepared union covers
        // every internal producer before any resource changes; no boundary survives the callback.
        submission.Batch(storage.SubmissionPrograms.Concat(density.SubmissionPrograms).Concat(work.SubmissionPrograms)
            .Concat(direct.SubmissionPrograms).Concat(diffuse.SubmissionPrograms),()=>
        {
            if(step is Step.Idle or Step.Density)
            {
                density.Submit(storage,input,map,admission.Encoding,.9f*lighting.Environment,current,chainRetained:true);
                step=Step.Density;
                if(!density.Chained)return;
                step=Step.Lighting;
            }
            if(step!=Step.Lighting)return;
            CloudVolumeCoordinates.Frustum(camera,CloudVolumeCoordinates.Origin(storage.Published.Domain,input),planes);
            if(!work.Select(storage,lighting,input.Frame,planes,current,settings.SelfShadowing,settings.Diffuse,density.Chained)||
                !work.TryBatch(storage,out batch)||!direct.Update(storage,work,current,deferStatus:true)||
                !diffuse.Update(storage,work,current,deferStatus:true))
                throw new InvalidOperationException(work.Failure??direct.Failure??diffuse.Failure??"Cloud lighting submission failed.");
            work.SubmitCompletion(storage,current);
            step=Step.Pending;context=RenderContextRegistry.Current();fence=GpuFence.Insert();
        });
    }
    /// <summary>Revokes queued publication and retires fences without waiting for the GPU.</summary>
    public void Dispose(){fence?.Dispose();fence=null;batch=null;step=Step.Idle;}
    #endregion
}
