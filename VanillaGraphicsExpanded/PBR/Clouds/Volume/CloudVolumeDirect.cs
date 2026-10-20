using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Updates retained dimensionless direct visibility against one frozen density/work batch.</summary>
internal sealed class CloudVolumeDirect(Func<int,CloudVolumeDirectProgram> createProgram):IDisposable
{
    private readonly CloudVolumeDirectProgram?[] programs=new CloudVolumeDirectProgram?[3];
    private readonly ulong[] executable=new ulong[3];
    private CloudVolumeWorkBatch? finished;
    private bool failed,disposed;
    internal string? Failure {get;private set;}
    #region Public API
    #region Transport
    /// <summary>Initializes incompatible source channels, submits ordered integrals and commits only barrier-complete work.</summary>
    internal bool Update(CloudVolumeStorage storage,CloudVolumeWork work,Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(failed||!work.TryBatch(storage,out var batch)||!current())return false;
        if(ReferenceEquals(finished,batch))return true;
        try
        {
            var selected=batch!;var fields=selected.Fields;int active=selected.ActiveSources;
            if(active==0){finished=selected;return true;}
            // Activation clears incompatible history everywhere, including jobs that will wait for fair service.
            // This bounded full-field memory pass has no ray integrals and is charged separately from the job budget.
            if(selected.NewlyActive!=0)
            {
                var p=Prepare(0);p.Capture(fields.Domain,default,selected.NewlyActive);Bind(p,fields);
                Run(p,()=>p.Dispatch(fields.Domain.BlocksX,fields.Domain.BlocksY,fields.Domain.BlocksX),current);
            }
            for(int source=0;source<2;source++)if((active&(1<<source))!=0)
            {
                var p=Prepare(source+1);p.Capture(fields.Domain,source==0?selected.Lighting.Sun:selected.Lighting.Moon,0);Bind(p,fields);
                // Separate submissions and image barriers serialize read-modify-write access to shared RG texels.
                Run(p,()=>p.DispatchIndirect(selected.Commands),current);
            }
            if(!work.Complete(storage,selected.Frame,active,()=>work.TryBatch(storage,out var live)&&ReferenceEquals(live,selected),current))
                throw new InvalidOperationException(work.Failure??"Direct transport completion certificate changed.");
            finished=selected;return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
    }
    #endregion
    #region Lifetime
    /// <summary>Releases executables at explicit recovery boundaries without owning shared fields.</summary>
    internal void Reset()
    {
        failed=false;Failure=null;finished=null;
        Action[] release=new Action[3];
        for(int i=0;i<3;i++){int slot=i;release[i]=()=>{programs[slot]?.Dispose();programs[slot]=null;executable[slot]=0;};}
        RenderingCleanup.Run(release);
    }
    /// <summary>Retires the private solver once.</summary>
    public void Dispose(){if(disposed)return;disposed=true;Reset();}
    #endregion
    #endregion
    #region Private
    /// <summary>Admits transport capabilities and retains one stable executable per used structural operation.</summary>
    private CloudVolumeDirectProgram Prepare(int operation)
    {
        var caps=GpuSupport.Graphics;
        if(caps.MaxUniformBlockSize<128||caps.MaxComputeShaderStorageBlocks<2||caps.MaxShaderStorageBufferBindings<2||
            caps.MaxComputeWorkGroupInvocations<256||caps.MaxComputeWorkGroupSize.Length<3||
            caps.MaxComputeWorkGroupSize[0]<8||caps.MaxComputeWorkGroupSize[1]<4||caps.MaxComputeWorkGroupSize[2]<8)
            throw new NotSupportedException("Stored direct transport exceeds compute capabilities.");
        var p=programs[operation]??=createProgram(operation);
        if(!p.EnsureReady())throw new InvalidOperationException(p.PreparationLog);
        ulong next=((IGpuProgram)p).ExecutableRevision;
        if(executable[operation]!=0&&executable[operation]!=next)throw new InvalidOperationException("Direct executable changed; reset required.");
        executable[operation]=next;return p;
    }
    /// <summary>Binds only candidate-owned density, visibility, occupancy and existing queue scratch.</summary>
    private static void Bind(CloudVolumeDirectProgram p,CloudVolumeFields fields)
    {p.Density=fields.Density;p.Occupancy=fields.Occupancy;p.Scratch=fields.Scratch;p.Direct=new(fields.Direct,TextureAccess.ReadWrite,Layered:true);}
    /// <summary>Orders image/channel writes and checks context/source certificates around each submission.</summary>
    private void Run(CloudVolumeDirectProgram p,Action dispatch,Func<bool> current)
    {
        Check(p,current);
        var bindings=p.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Direct bindings unavailable.");
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.Volume.Direct"),EngineBoundaryResources.From(bindings),_=>
            {Barrier();dispatch();Barrier();}))throw new InvalidOperationException("Direct submission boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud volume direct");Check(p,current);
    }
    /// <summary>Rejects changed source or executable/context identities before publication.</summary>
    private void Check(CloudVolumeDirectProgram p,Func<bool> current)
    {
        var program=(IGpuProgram)p;
        if(!current()||!p.IsValid||program.ExecutableContext!=RenderContextRegistry.Current())
            throw new InvalidOperationException("Direct source or executable context changed.");
        for(int i=0;i<3;i++)if(programs[i] is {} loaded&&((IGpuProgram)loaded).ExecutableRevision!=executable[i])
            throw new InvalidOperationException("Direct executable changed during transport.");
    }
    /// <summary>Makes ordered image writes, sampled fields and failure bits visible to their consumers.</summary>
    private static void Barrier()=>GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|
        MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit|
        MemoryBarrierFlags.CommandBarrierBit|MemoryBarrierFlags.TextureUpdateBarrierBit);
    #endregion
}
