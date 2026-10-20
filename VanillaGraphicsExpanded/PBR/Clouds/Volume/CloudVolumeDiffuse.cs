using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Refines bounded diffuse jobs from a coherent old field and publishes only a complete destination/scale pair.</summary>
internal sealed class CloudVolumeDiffuse(Func<bool,CloudVolumeDiffuseProgram> createProgram):IDisposable
{
    private readonly CloudVolumeDiffuseProgram?[] programs=new CloudVolumeDiffuseProgram?[2];
    private readonly ulong[] executable=new ulong[2];
    private CloudVolumeWorkBatch? finished;
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
            finished.Fields.Revision==(ulong)finished.FieldRevision+1&&
            finished.Fields.DensityState.Revision==finished.DensityRevision&&
            finished.Fields.DensityState.CompletedFrame==finished.DensityFrame)return true;
        if(!valid)return false;
        LastJobs=0;LastCopyBytes=0;
        try
        {
            var batch=selected!;var fields=batch.Fields;
            if(!batch.Diffuse){finished=batch;return true;}
            if(!work.DirectCompleted)throw new InvalidOperationException("Diffuse work requires ordered direct completion.");
            // One scalar count avoids a full-field copy for an empty GPU-selected list; no jobs are read back or CPU-selected.
            using(var count=fields.Scratch.MapScope<uint>(17*4,1,MapBufferAccessMask.MapReadBit))LastJobs=checked((int)count.Span[0]);
            if(LastJobs is <0 or >32)throw new InvalidOperationException("Diffuse list exceeds its admitted budget.");
            if(LastJobs==0){finished=batch;return true;}
            var bounds=CloudVolumeLightingBounds.Admit(batch.Lighting,fields.LightingBounds,fields.Encoding);
            var p=Prepare(batch.Direct);p.Capture(batch,bounds.Encoding);
            p.Density=fields.Density;p.Direct=batch.Direct?fields.Direct:null;p.Source=fields.Diffuse;
            p.Occupancy=fields.Occupancy;p.Scratch=fields.Scratch;
            p.Destination=new(fields.DiffuseSpare,TextureAccess.WriteOnly,Layered:true);
            if(!storage.UpdateDiffuse(bounds.Encoding,current,_=>
                {
                    LastCopyBytes=fields.Domain.CopyBytes;
                    Run(p,batch,current);
                },()=>work.Complete(storage,batch.Frame,4,()=>work.TryBatch(storage,out var live)&&ReferenceEquals(live,batch),current,deferStatus)))
                throw new InvalidOperationException(work.Failure??"Diffuse destination publication failed.");
            fields.LightingBounds=bounds;finished=batch;return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
    }
    #endregion
    #region Lifetime
    /// <summary>Releases solver executables without discarding bounds owned by retained field publications.</summary>
    internal void Reset()
    {
        failed=false;Failure=null;finished=null;LastJobs=0;LastCopyBytes=0;
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
            caps.MaxComputeWorkGroupSize[0]<8||caps.MaxComputeWorkGroupSize[1]<4||caps.MaxComputeWorkGroupSize[2]<8)
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
        var bindings=p.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Diffuse bindings unavailable.");
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.Volume.Diffuse"),EngineBoundaryResources.From(bindings),_=>
            {Barrier();p.DispatchIndirect(batch.Commands,12);Barrier();}))throw new InvalidOperationException("Diffuse submission boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud volume diffuse");Check(p,current);
    }
    /// <summary>Checks exact executable and source/context certificates before accepting writes.</summary>
    private void Check(CloudVolumeDiffuseProgram p,Func<bool> current)
    {
        if(!current()||!p.IsValid||((IGpuProgram)p).ExecutableContext!=RenderContextRegistry.Current())
            throw new InvalidOperationException("Diffuse source or context changed.");
        for(int i=0;i<2;i++)if(programs[i] is {} loaded&&((IGpuProgram)loaded).ExecutableRevision!=executable[i])
            throw new InvalidOperationException("Diffuse executable changed during transport.");
    }
    /// <summary>Closes sampled-field, image, queue and failure-status visibility across transport passes.</summary>
    private static void Barrier()=>GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|
        MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit|
        MemoryBarrierFlags.CommandBarrierBit|MemoryBarrierFlags.TextureUpdateBarrierBit);
    #endregion
}
