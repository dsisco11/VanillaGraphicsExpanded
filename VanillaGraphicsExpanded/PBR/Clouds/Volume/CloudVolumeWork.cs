using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns fair lighting queues while separating selection, completed work and compatible retained history.</summary>
internal sealed class CloudVolumeWork(Func<int,bool,PackedUniformBuffer?,CloudVolumeWorkProgram> createProgram,CloudVolumeSubmission? submission=null,int quality=2):IDisposable
{
    private int directions=CloudVolumeQuality.Resolve(quality).Directions;
    private readonly CloudVolumeSubmission submission=submission??new();
    /// <summary>Exposes prepared executable footprints to the enclosing update boundary.</summary>
    internal System.Collections.Generic.IEnumerable<GpuComputeProgram> SubmissionPrograms=>System.Linq.Enumerable.OfType<GpuComputeProgram>(programs);
    internal const int DirectListWord=16384,DiffuseListWord=16640,SummaryA=32768,SummaryB=800000;
    /// <summary>Reserves retained repair history outside density selector temporaries and light lists.</summary>
    internal const int RepairCountWord=38,CopyVoxelsWord=39,RepairListWord=17280;
    private readonly CloudVolumeWorkProgram?[] programs=new CloudVolumeWorkProgram?[11];
    private readonly ulong[] executable=new ulong[11];
    private readonly Vector4[] planes=new Vector4[6];
    private GpuIndirectBuffer? commands;
    private PackedUniformBuffer? inputs;
    private CloudVolumeWorkBatch? batch;
    private CloudVolumeFields? retainedFields;
    private int retainedSources;
    private uint revision;
    private bool largeBudget;
    private int completed,pendingCompletion;
    private bool failed,disposed;
    internal bool Ready {get;private set;}
    internal string? Failure {get;private set;}
    /// <summary>Reports actual copied texel traffic from the existing fenced status read.</summary>
    internal long LastDiffuseCopyBytes {get;private set;}
    /// <summary>Reports exact origin-ray charges, or conservative upper charges for explicitly unknown jobs.</summary>
    internal CloudVolumeWorkStatistics Statistics {get;private set;}
    /// <summary>Reports ordered direct submission; GPU barriers, rather than a CPU wait, precede diffuse reads.</summary>
    internal bool DirectOrdered=>batch is not null&&(completed&batch.ActiveSources)==batch.ActiveSources;

    #region Public API
    #region Selection and completion
    /// <summary>Admits source changes and emits bounded GPU work without modifying actual-completion stamps.</summary>
    internal bool Select(CloudVolumeStorage storage,AtmosphereLighting lighting,uint frame,ReadOnlySpan<Vector4> frustum,
        Func<bool> current,bool directEnabled=true,bool diffuseEnabled=true,bool densityPending=false,int lightingRays=CloudVolumeLightingBudget.DefaultRays)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(failed||!storage.Ready||!current()){Ready=false;return false;}
        var fields=storage.Published;
        if(batch is not null&&ReferenceEquals(batch.Fields,fields)&&batch.Frame==frame)return Ready;
        Ready=false;
        try
        {
            Validate(lighting,frustum);
            lightingRays=CloudVolumeLightingBudget.Sanitize(lightingRays);
            Prepare(lightingRays);
            bool same=batch is not null&&ReferenceEquals(batch.Fields,fields);
            bool retained=ReferenceEquals(retainedFields,fields);
            if((same&&frame<batch!.Frame)||((same||retained)&&revision==uint.MaxValue))throw new InvalidOperationException("Cloud work counters require reset.");
            if(!same&&!retained)revision=0;
            revision++;
            int active=directEnabled?((lighting.Solar!=Vector3.Zero?1:0)|(lighting.Lunar!=Vector3.Zero?2:0)):0;
            int newly=active&~(same?batch!.ActiveSources:ReferenceEquals(retainedFields,fields)?retainedSources:0),dirty=same?0:7;
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
            SelectLists(fields,frame,active,dirty,newly,diffuseEnabled,current,densityPending,lightingRays);
            batch=new(fields,lighting,frame,revision,fields.Revision,fields.DensityState.Revision,
                fields.DensityState.CompletedFrame,active,newly,directEnabled,diffuseEnabled,commands!);
            retainedFields=null;retainedSources=0;
            completed=pendingCompletion=0;Ready=true;return true;
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
            if(deferStatus)pendingCompletion|=channels;
            else {CommitChannels(selected,channels,current);VerifyStatus(selected.Fields,channels);}
            completed|=channels;return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Ready=false;Failure=error.Message;storage.Withdraw();return false;}
    }
    /// <summary>Submits one successful-only GPU completion after all dependent transport passes, without CPU inspection.</summary>
    internal void SubmitCompletion(CloudVolumeStorage storage,Func<bool> current)
    {
        if(!TryBatch(storage,out var selected))throw new InvalidOperationException("Cloud completion batch changed.");
        if(pendingCompletion==0)return;
        CommitChannels(selected!,pendingCompletion,current);pendingCompletion=0;
    }
    /// <summary>Reads accumulated failures and the diffuse count together after the final producer fence has signaled.</summary>
    internal int VerifyStatus(CloudVolumeFields fields,int channels=7)
    {
        Check(()=>true);
        using var status=fields.Scratch.MapScope<uint>(17*4,31,MapBufferAccessMask.MapReadBit);
        if((status.Span[8]&((uint)channels|8u))!=0)throw new InvalidOperationException("Cloud density or lighting arithmetic failed.");
        if(status.Span[0]>CloudVolumeLightingBudget.MaximumDiffuseJobs)throw new InvalidOperationException("Diffuse list exceeds its admitted budget.");
        if(status.Span[21]>CloudVolumeLightingBudget.MaximumDiffuseJobs||status.Span[22]>fields.Domain.Voxels)
            throw new InvalidOperationException("Diffuse repair history or copy extent exceeds its admitted bound.");
        LastDiffuseCopyBytes=16L*status.Span[22];
        Statistics=new(status.Span[23],status.Span[24],status.Span[25],status.Span[26],status.Span[27],status.Span[28],status.Span[29],status.Span[30]);
        if(Statistics.DirectRays>Statistics.DirectAllowance||Statistics.DiffuseRays>Statistics.DiffuseAllowance)
            throw new InvalidOperationException("Occupied cloud rays exceed their frozen solver allowance.");
        return (int)status.Span[0];
    }
    #endregion

    #region Lifetime
    /// <summary>Replaces profile executables without falsely reactivating already valid direct channels.</summary>
    internal void Reconfigure(int nextQuality=2)
    {
        var fields=batch?.Fields??retainedFields;int sources=batch?.ActiveSources??retainedSources;uint priorRevision=revision;
        Reset();retainedFields=fields;retainedSources=sources;revision=priorRevision;
        directions=CloudVolumeQuality.Resolve(nextQuality).Directions;
    }
    /// <summary>Revokes queued certificates and programs while leaving field ownership with storage.</summary>
    internal void Reset()
    {
        Ready=failed=false;Failure=null;batch=null;retainedFields=null;retainedSources=0;revision=0;completed=pendingCompletion=0;LastDiffuseCopyBytes=0;Statistics=default;
        Action[] release=new Action[13];
        for(int i=0;i<programs.Length;i++){int slot=i;release[i]=()=>{programs[slot]?.Dispose();programs[slot]=null;};}
        release[11]=()=>{commands?.Dispose();commands=null;};
        release[12]=()=>{inputs?.Dispose();inputs=null;};RenderingCleanup.Run(release);
    }
    /// <summary>Retires the queue owner once without taking ownership of shared lighting publications.</summary>
    public void Dispose(){if(disposed)return;disposed=true;Reset();}
    #endregion
    #endregion

    #region Public API
    /// <summary>Admits fixed work-list capabilities and rejects executable changes while retaining queue history.</summary>
    internal void Prepare(int lightingRays=0)
    {
        if(lightingRays!=0)
        {
            bool large=CloudVolumeLightingBudget.Candidates(lightingRays,true)>64||CloudVolumeLightingBudget.Candidates(lightingRays,false,directions)>64;
            if(large!=largeBudget)
            {
                // Selection runs only after the preceding update retires. Changing capacity retains
                // region demand, fair cursors and source history while replacing private executables.
                for(int i=0;i<programs.Length;i++){programs[i]?.Dispose();programs[i]=null;executable[i]=0;}
                largeBudget=large;
            }
        }
        var caps=GpuSupport.Graphics;
        if(caps.MaxUniformBlockSize<208||caps.MaxComputeShaderStorageBlocks<4||caps.MaxShaderStorageBufferBindings<4||
            caps.MaxComputeWorkGroupInvocations<256||caps.MaxComputeWorkGroupSize.Length<1||caps.MaxComputeWorkGroupSize[0]<256||
            caps.MaxComputeSharedMemorySize<(largeBudget?12304:3088))
            throw new NotSupportedException("Cloud work requires four storage bindings, 256 lanes and shared selection storage.");
        for(int i=0;i<programs.Length;i++)
        {
            var p=programs[i]??=createProgram(i,largeBudget,inputs??=new PackedUniformBuffer(208));if(!p.EnsureReady())throw new InvalidOperationException(p.PreparationLog);
            ulong next=((IGpuProgram)p).ExecutableRevision;
            if(batch is not null&&executable[i]!=0&&executable[i]!=next)throw new InvalidOperationException("Cloud work executable changed; reset required.");
            executable[i]=next;
        }
        if(commands is null){commands=GpuIndirectBuffer.Create();commands.UploadData(new uint[45]);}
    }
    #endregion
    #region Private
    #region Program preparation and execution

    /// <summary>Classifies in parallel and reduces disjoint top-K summaries without full-domain serial scans.</summary>
    private void SelectLists(CloudVolumeFields fields,uint frame,int active,int dirty,int newly,bool diffuse,Func<bool> current,bool densityPending,int lightingRays)
    {
        var d=fields.Domain;
        int direct=active==0?0:(d.Blocks*2+255)/256,indirect=diffuse?(d.Blocks+255)/256:0;
        int directCandidates=CloudVolumeLightingBudget.Candidates(lightingRays,true),diffuseCandidates=CloudVolumeLightingBudget.Candidates(lightingRays,false,directions);
        int directStride=(directCandidates+1)/2+3*directCandidates+1,diffuseStride=(diffuseCandidates+1)/2+3*diffuseCandidates+1;
        // Persistent membership occupies the unused scratch tail, above all reduction temporaries.
        if(SummaryA+direct*directStride+indirect*diffuseStride>SummaryB||
            SummaryB+((direct+3)/4)*directStride+((indirect+3)/4)*diffuseStride>970000||
            970576+d.Blocks>(CloudVolumeDomain.ScratchBytes-4096)/4||(d.Blocks+255)/256>288)
            throw new NotSupportedException("Cloud work summaries or occupied list exceed fixed scratch.");
        /// <summary>Orders one operation with fixed or GPU-produced dispatch dimensions.</summary>
        void Submit(int op,int count,int source=0,int destination=0,int level=0,int indirectOffset=-1)
        {
            var p=programs[op]!;
            p.Capture(d,frame,revision,active,dirty,newly,diffuse,0,planes,source,destination,
                densityPending:densityPending,lightingRays:lightingRays,selectionLevel:level);
            Bind(op,fields);
            if(indirectOffset<0)Run(op,count);
            else submission.Run(p,()=>p.DispatchIndirect(commands!,indirectOffset));
        }
        Check(current);Submit(0,1);Submit(1,(d.Blocks+255)/256);
        // Only density membership changes trigger compaction. No CPU list-length readback
        // is needed: stable occupancy produces zero count, prefix and scatter groups.
        Submit(6,1);Submit(7,0,indirectOffset:144);Submit(8,0,indirectOffset:168);
        Submit(9,0,indirectOffset:156);Submit(10,1);
        int source=SummaryA,destination=SummaryB,level=0;
        Submit(2,0,0,source,indirectOffset:72);
        while(direct>1||indirect>1)
        {
            level++;
            Submit(3,0,source,destination,level,(18+level*3)*4);
            direct=(direct+3)/4;indirect=(indirect+3)/4;(source,destination)=(destination,source);
        }
        Submit(4,1,source,level:level);Check(current);
    }
    /// <summary>Commits frozen channels only after all earlier producer writes and accumulated failure bits are visible.</summary>
    private void CommitChannels(CloudVolumeWorkBatch selected,int channels,Func<bool> current)
    {
        Check(current);
        programs[5]!.Capture(selected.Fields.Domain,selected.Frame,selected.Revision,selected.ActiveSources,0,
            selected.NewlyActive,selected.Diffuse,channels,planes);
        Bind(5,selected.Fields);Run(5);Check(current);
    }
    /// <summary>Binds exact field metadata, existing scratch and initialized indirect ranges.</summary>
    private void Bind(int op,CloudVolumeFields fields)
    {programs[op]!.Regions=fields.Regions;programs[op]!.Scratch=fields.Scratch;programs[op]!.Commands=new(commands,0,180);programs[op]!.Occupancy=fields.Occupancy;}
    /// <summary>Submits typed work through resource authority and closes metadata/command visibility.</summary>
    private void Run(int op,int groups=1)
    {
        var p=programs[op]!;submission.Run(p,()=>p.Dispatch(groups));
    }
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
        for(int i=0;i<programs.Length;i++)if(programs[i] is not {IsValid:true} p||((IGpuProgram)p).ExecutableRevision!=executable[i]||
            ((IGpuProgram)p).ExecutableContext!=context)throw new InvalidOperationException("Cloud work executable/context changed.");
    }
    /// <summary>Rejects nonfinite channels before retained source comparisons.</summary>
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    #endregion
    #endregion
}
