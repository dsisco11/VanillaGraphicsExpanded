using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns fair lighting queues while separating selection, completed work and compatible retained history.</summary>
internal sealed class CloudVolumeWork(Func<int,CloudVolumeWorkProgram> createProgram):IDisposable
{
    internal const int DirectListWord=16384,DiffuseListWord=16448,SummaryA=32768,SummaryB=196608;
    private readonly CloudVolumeWorkProgram?[] programs=new CloudVolumeWorkProgram?[6];
    private readonly ulong[] executable=new ulong[6];
    private readonly Vector4[] planes=new Vector4[6];
    private GpuIndirectBuffer? commands;
    private CloudVolumeWorkBatch? batch;
    private uint revision;
    private int completed;
    private bool failed,disposed;
    internal bool Ready {get;private set;}
    internal string? Failure {get;private set;}
    /// <summary>Reports whether every relevant direct channel finished before a diffuse gather can read it.</summary>
    internal bool DirectCompleted=>batch is not null&&(completed&batch.ActiveSources)==batch.ActiveSources;

    #region Public API
    #region Selection and completion
    /// <summary>Admits source changes and emits bounded GPU work without modifying actual-completion stamps.</summary>
    internal bool Select(CloudVolumeStorage storage,AtmosphereLighting lighting,uint frame,ReadOnlySpan<Vector4> frustum,
        Func<bool> current,bool directEnabled=true,bool diffuseEnabled=true)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(failed||!storage.Ready||!current()){Ready=false;return false;}
        var fields=storage.Published;
        if(batch is not null&&ReferenceEquals(batch.Fields,fields)&&batch.Frame==frame)return Ready;
        Ready=false;
        try
        {
            Validate(lighting,frustum);Prepare();
            bool same=batch is not null&&ReferenceEquals(batch.Fields,fields);
            if(same&&(frame<batch!.Frame||revision==uint.MaxValue))throw new InvalidOperationException("Cloud work counters require reset.");
            if(!same)revision=0;
            revision++;
            int active=directEnabled?((lighting.Solar!=Vector3.Zero?1:0)|(lighting.Lunar!=Vector3.Zero?2:0)):0;
            int newly=active&~(same?batch!.ActiveSources:0),dirty=same?0:7;
            if(same)
            {
                var prior=batch!;
                if(lighting.Sun!=prior.Lighting.Sun)dirty|=5;
                if(lighting.Moon!=prior.Lighting.Moon)dirty|=6;
                if(lighting.Solar!=prior.Lighting.Solar||lighting.Lunar!=prior.Lighting.Lunar||
                    lighting.Environment!=prior.Lighting.Environment||directEnabled!=prior.Direct||diffuseEnabled!=prior.Diffuse)dirty|=4;
            }
            dirty|=newly;
            frustum.CopyTo(planes);
            SelectLists(fields,frame,active,dirty,newly,diffuseEnabled,current);
            batch=new(fields,lighting,frame,revision,fields.Revision,fields.DensityState.Revision,
                fields.DensityState.CompletedFrame,active,newly,directEnabled,diffuseEnabled,commands!);
            completed=0;Ready=true;return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
    }

    /// <summary>Returns only the selected batch whose field and density identities remain unchanged.</summary>
    internal bool TryBatch(CloudVolumeStorage storage,out CloudVolumeWorkBatch? result)
    {
        result=batch;
        return Ready&&storage.Ready&&batch is not null&&ReferenceEquals(batch.Fields,storage.Published)&&
            batch.FieldRevision==batch.Fields.Revision&&batch.DensityRevision==batch.Fields.DensityState.Revision&&
            batch.DensityFrame==batch.Fields.DensityState.CompletedFrame;
    }

    /// <summary>Commits explicitly completed channels after solvers and barriers; failed work cannot become fresh.</summary>
    internal bool Complete(CloudVolumeStorage storage,uint frame,int channels,Func<bool> succeeded,Func<bool> current,bool deferStatus=false)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!TryBatch(storage,out var selected)||selected!.Frame!=frame||channels is <1 or >7||
            (completed&channels)!=0||(channels&~(selected.ActiveSources|(selected.Diffuse?4:0)))!=0)return false;
        try
        {
            Check(current);if(!succeeded())throw new InvalidOperationException("Cloud lighting work did not complete successfully.");
            var fields=selected.Fields;
            programs[5]!.Capture(fields.Domain,frame,selected.Revision,selected.ActiveSources,0,selected.NewlyActive,selected.Diffuse,channels,planes);
            Bind(5,fields);Run(5);Check(current);
            if(!deferStatus)VerifyStatus(fields,channels);
            completed|=channels;return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Ready=false;Failure=error.Message;storage.Withdraw();return false;}
    }
    /// <summary>Validates accumulated arithmetic failures after the caller's GPU completion fence has signaled.</summary>
    internal void VerifyStatus(CloudVolumeFields fields,int channels=7)
    {
        Check(()=>true);
        using var status=fields.Scratch.MapScope<uint>(25*4,1,MapBufferAccessMask.MapReadBit);
        if((status.Span[0]&(uint)channels)!=0)throw new InvalidOperationException("Cloud lighting arithmetic failed.");
    }
    #endregion

    #region Lifetime
    /// <summary>Revokes queued certificates and programs while leaving field ownership with storage.</summary>
    internal void Reset()
    {
        Ready=failed=false;Failure=null;batch=null;revision=0;completed=0;
        Action[] release=new Action[7];
        for(int i=0;i<6;i++){int slot=i;release[i]=()=>{programs[slot]?.Dispose();programs[slot]=null;};}
        release[6]=()=>{commands?.Dispose();commands=null;};RenderingCleanup.Run(release);
    }
    /// <summary>Retires the queue owner once without taking ownership of shared lighting publications.</summary>
    public void Dispose(){if(disposed)return;disposed=true;Reset();}
    #endregion
    #endregion

    #region Public API
    /// <summary>Admits fixed work-list capabilities and rejects executable changes while retaining queue history.</summary>
    internal void Prepare()
    {
        var caps=GpuSupport.Graphics;
        if(caps.MaxUniformBlockSize<208||caps.MaxComputeShaderStorageBlocks<4||caps.MaxShaderStorageBufferBindings<4||
            caps.MaxComputeWorkGroupInvocations<256||caps.MaxComputeWorkGroupSize.Length<1||caps.MaxComputeWorkGroupSize[0]<256||
            caps.MaxComputeSharedMemorySize<4112)
            throw new NotSupportedException("Cloud work requires four storage bindings, 256 lanes and shared selection storage.");
        for(int i=0;i<6;i++)
        {
            var p=programs[i]??=createProgram(i);if(!p.EnsureReady())throw new InvalidOperationException(p.PreparationLog);
            ulong next=((IGpuProgram)p).ExecutableRevision;
            if(batch is not null&&executable[i]!=next)throw new InvalidOperationException("Cloud work executable changed; reset required.");
            executable[i]=next;
        }
        if(commands is null){commands=GpuIndirectBuffer.Create();commands.UploadData(new uint[]{0,1,1,0,1,1,0,1,1,0,1,1});}
    }
    #endregion
    #region Private
    #region Program preparation and execution

    /// <summary>Classifies in parallel and reduces disjoint top-K summaries without full-domain serial scans.</summary>
    private void SelectLists(CloudVolumeFields fields,uint frame,int active,int dirty,int newly,bool diffuse,Func<bool> current)
    {
        var d=fields.Domain;
        int direct=active==0?0:(d.Blocks*2+255)/256,indirect=diffuse?(d.Blocks+255)/256:0;
        // The maximum first level ends at word 190971; its quarter-sized spare ends below 236356.
        // Both occupy existing fixed scratch, disjoint from density lists and lighting headers.
        if(SummaryA+direct*225+indirect*113>SummaryB||SummaryB+((direct+3)/4)*225+((indirect+3)/4)*113>
            (CloudVolumeDomain.ScratchBytes-4096)/4)throw new NotSupportedException("Cloud work summaries exceed fixed scratch.");
        void Submit(int op,int count,int source=0,int destination=0,int dg=0,int ig=0)
        {
            programs[op]!.Capture(d,frame,revision,active,dirty,newly,diffuse,0,planes,source,destination,dg,ig);
            Bind(op,fields);Run(op,count);
        }
        Check(current);Submit(0,1);Submit(1,(d.Blocks+255)/256);
        int source=SummaryA,destination=SummaryB;
        if(direct+indirect>0)Submit(2,direct+indirect,0,source,direct,indirect);
        while(direct>1||indirect>1)
        {
            Submit(3,(direct+3)/4+(indirect+3)/4,source,destination,direct,indirect);
            direct=(direct+3)/4;indirect=(indirect+3)/4;(source,destination)=(destination,source);
        }
        Submit(4,1,source,0,direct,indirect);Check(current);
    }
    /// <summary>Binds exact field metadata, existing scratch and initialized indirect ranges.</summary>
    private void Bind(int op,CloudVolumeFields fields)
    {programs[op]!.Regions=fields.Regions;programs[op]!.Scratch=fields.Scratch;programs[op]!.Commands=new(commands,0,48);programs[op]!.Occupancy=fields.Occupancy;}
    /// <summary>Submits typed work through resource authority and closes metadata/command visibility.</summary>
    private void Run(int op,int groups=1)
    {
        var p=programs[op]!;var bindings=p.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Cloud work bindings unavailable.");
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.Volume.Work"),EngineBoundaryResources.From(bindings),_=>
            {Barrier();p.Dispatch(groups);Barrier();}))throw new InvalidOperationException("Cloud work boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud volume work");
    }
    /// <summary>Orders retained metadata and indirect commands before solvers or CPU status inspection.</summary>
    private static void Barrier()=>GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|
        MemoryBarrierFlags.BufferUpdateBarrierBit|MemoryBarrierFlags.CommandBarrierBit|MemoryBarrierFlags.ShaderImageAccessBarrierBit|
        MemoryBarrierFlags.TextureFetchBarrierBit);
    #endregion

    #region Input and identity validation
    /// <summary>Admits physical source values and six finite native-local frustum planes independently of fog/exposure.</summary>
    private static void Validate(AtmosphereLighting lighting,ReadOnlySpan<Vector4> frustum)
    {
        if(frustum.Length!=6)throw new ArgumentException("Six cloud-local planes are required.",nameof(frustum));
        foreach(var value in new[]{lighting.Solar,lighting.Lunar,lighting.Environment})
            if(!Finite(value)||Vector3.Min(value,Vector3.Zero)!=Vector3.Zero)throw new ArgumentOutOfRangeException(nameof(lighting));
        foreach(var direction in new[]{lighting.Sun,lighting.Moon})
            if(!Finite(direction))throw new ArgumentOutOfRangeException(nameof(lighting));
        if((lighting.Solar!=Vector3.Zero&&Math.Abs(lighting.Sun.LengthSquared()-1)>.0001)||
            (lighting.Lunar!=Vector3.Zero&&Math.Abs(lighting.Moon.LengthSquared()-1)>.0001))throw new ArgumentOutOfRangeException(nameof(lighting));
        foreach(var plane in frustum)if(!Finite(new(plane.X,plane.Y,plane.Z))||!float.IsFinite(plane.W))throw new ArgumentOutOfRangeException(nameof(frustum));
    }
    /// <summary>Checks all source-publication and executable/context certificates on both sides of work.</summary>
    private void Check(Func<bool> current)
    {
        if(!current())throw new InvalidOperationException("Cloud work publication changed.");
        var context=RenderContextRegistry.Current();
        for(int i=0;i<6;i++)if(programs[i] is not {IsValid:true} p||((IGpuProgram)p).ExecutableRevision!=executable[i]||
            ((IGpuProgram)p).ExecutableContext!=context)throw new InvalidOperationException("Cloud work executable/context changed.");
    }
    /// <summary>Rejects nonfinite channels before retained source comparisons.</summary>
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    #endregion
    #endregion
}
