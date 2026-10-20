using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Refines bounded diffuse jobs from a coherent old field and publishes only a complete destination/scale pair.</summary>
internal sealed class CloudVolumeDiffuse(Func<bool,CloudVolumeDiffuseProgram> createProgram,CloudVolumeSubmission? submission=null):IDisposable
{
    private readonly CloudVolumeSubmission submission=submission??new();
    /// <summary>Exposes prepared executable footprints to the enclosing update boundary.</summary>
    internal System.Collections.Generic.IEnumerable<GpuComputeProgram> SubmissionPrograms=>System.Linq.Enumerable.OfType<GpuComputeProgram>(programs);
    private readonly CloudVolumeDiffuseProgram?[] programs=new CloudVolumeDiffuseProgram?[2];
    private readonly ulong[] executable=new ulong[2];
    private CloudVolumeWorkBatch? finished;
    private CloudVolumeWorkBatch? pending;
    private CloudVolumeLightingBounds? pendingBounds;
    private bool failed,disposed;
    internal string? Failure {get;private set;}
    internal int LastJobs {get;private set;}
    internal long LastCopyBytes {get;private set;}
    #region Public API
    #region Transport and publication
    /// <summary>Copies once for a nonempty batch, gathers into the spare, then commits metadata before the atomic field publication.</summary>
    internal bool Update(CloudVolumeStorage storage,CloudVolumeWork work,Func<bool> current,bool deferStatus=false)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        bool valid=work.TryBatch(storage,out var selected);
        if(failed||!storage.Ready||!current())return false;
        // A successful swap advances field revision, intentionally retiring the old queue certificate.
        if(ReferenceEquals(finished,selected)&&finished is not null&&ReferenceEquals(storage.Published,finished.Fields)&&
            (finished.Fields.Revision==finished.FieldRevision||finished.Fields.Revision==(ulong)finished.FieldRevision+1)&&
            finished.Fields.DensityState.Revision==finished.DensityRevision&&
            finished.Fields.DensityState.CompletedFrame==finished.DensityFrame)return true;
        if(!valid)return false;
        LastJobs=0;LastCopyBytes=0;
        try
        {
            var batch=selected!;var fields=batch.Fields;
            if(!batch.Diffuse){finished=batch;return true;}
            if(ReferenceEquals(pending,batch))return true;
            if(pending is not null)throw new InvalidOperationException("A diffuse publication is already pending.");
            if(!work.DirectOrdered)throw new InvalidOperationException("Diffuse work requires ordered direct submission.");
            var bounds=CloudVolumeLightingBounds.Admit(batch.Lighting,fields.LightingBounds,fields.Encoding);
            var p=Prepare(batch.Direct);p.Capture(batch,bounds.Encoding);
            p.Density=fields.Density;p.Direct=batch.Direct?fields.Direct:null;p.Source=fields.Diffuse;
            p.Occupancy=fields.Occupancy;p.Scratch=fields.Scratch;
            p.Destination=new(fields.DiffuseSpare,TextureAccess.WriteOnly,Layered:true);
            if(!storage.UpdateDiffuse(bounds.Encoding,current,_=>
                {
                    Run(p,batch,current);
                },()=>work.Complete(storage,batch.Frame,4,()=>work.TryBatch(storage,out var live)&&ReferenceEquals(live,batch),current,deferStatus),
                batch.Commands,deferPublication:true))
                throw new InvalidOperationException(work.Failure??"Diffuse destination publication failed.");
            pending=batch;pendingBounds=bounds;
            return deferStatus||Complete(storage,work,current);
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
    }
    /// <summary>Reads final status/count and swaps only a successful nonempty staged destination.</summary>
    internal bool Complete(CloudVolumeStorage storage,CloudVolumeWork work,Func<bool> current)
    {
        try
        {
            if(!work.TryBatch(storage,out var selected)||!current())throw new InvalidOperationException("Diffuse completion batch changed.");
            int jobs=work.VerifyStatus(selected!.Fields);
            if(pending is null)return true;
            if(!ReferenceEquals(selected,pending))throw new InvalidOperationException("Staged diffuse certificate changed.");
            Check(programs[pending.Direct?1:0]!,current);
            var bounds=pendingBounds!;LastJobs=jobs;
            // Zero indirect copy/gather groups leave the spare untouched. In particular, never
            // publish that spare or a larger encoding for an empty selection.
            if(jobs!=0)
            {
                storage.CommitDiffuse(pending.Fields,bounds.Encoding,current);
                pending.Fields.LightingBounds=bounds;LastCopyBytes=pending.Fields.Domain.CopyBytes;
            }
            finished=pending;pending=null;pendingBounds=null;return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
    }
    #endregion
    #region Lifetime
    /// <summary>Releases solver executables without discarding bounds owned by retained field publications.</summary>
    internal void Reset()
    {
        failed=false;Failure=null;finished=pending=null;pendingBounds=null;LastJobs=0;LastCopyBytes=0;
        RenderingCleanup.Run(()=>{programs[0]?.Dispose();programs[0]=null;executable[0]=0;},
            ()=>{programs[1]?.Dispose();programs[1]=null;executable[1]=0;});
    }
    /// <summary>Retires the private transport owner once.</summary>
    public void Dispose(){if(disposed)return;disposed=true;Reset();}
    #endregion
    #endregion
    #region Public API
    /// <summary>Admits the gather shape and lazily creates only the selected compiled shadow path.</summary>
    internal CloudVolumeDiffuseProgram Prepare(bool shadows)
    {
        var caps=GpuSupport.Graphics;
        if(caps.MaxUniformBlockSize<208||caps.MaxComputeTextureImageUnits<(shadows?3:2)||
            caps.MaxCombinedTextureImageUnits<(shadows?3:2)||caps.MaxComputeShaderStorageBlocks<2||caps.MaxShaderStorageBufferBindings<2||
            caps.MaxComputeWorkGroupInvocations<256||caps.MaxComputeWorkGroupSize.Length<3||
            caps.MaxComputeWorkGroupSize[0]<256||caps.MaxComputeSharedMemorySize<4608||caps.MaxComputeWorkGroupSize[1]<4||caps.MaxComputeWorkGroupSize[2]<8)
            throw new NotSupportedException("Stored diffuse transport exceeds compute capabilities.");
        int index=shadows?1:0;var p=programs[index]??=createProgram(shadows);
        if(!p.EnsureReady())throw new InvalidOperationException(p.PreparationLog);
        ulong next=((IGpuProgram)p).ExecutableRevision;
        if(executable[index]!=0&&executable[index]!=next)throw new InvalidOperationException("Diffuse executable changed; reset required.");
        executable[index]=next;return p;
    }
    #endregion
    #region Private

    /// <summary>Orders coherent sampled inputs and spare writes through the existing graphics boundary.</summary>
    private void Run(CloudVolumeDiffuseProgram p,CloudVolumeWorkBatch batch,Func<bool> current)
    {
        Check(p,current);
        submission.Run(p,()=>p.DispatchIndirect(batch.Commands,12));Check(p,current);
    }
    /// <summary>Checks exact executable and source/context certificates before accepting writes.</summary>
    private void Check(CloudVolumeDiffuseProgram p,Func<bool> current)
    {
        if(!current()||!p.IsValid||((IGpuProgram)p).ExecutableContext!=RenderContextRegistry.Current())
            throw new InvalidOperationException("Diffuse source or context changed.");
        for(int i=0;i<2;i++)if(programs[i] is {} loaded&&((IGpuProgram)loaded).ExecutableRevision!=executable[i])
            throw new InvalidOperationException("Diffuse executable changed during transport.");
    }
    #endregion
}
