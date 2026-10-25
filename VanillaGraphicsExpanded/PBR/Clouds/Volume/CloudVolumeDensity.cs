using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns certified descriptor observation, bounded density jobs and barrier-complete occupancy publication.</summary>
internal sealed class CloudVolumeDensity(Func<int,PackedUniformBuffer?,CloudVolumeDensityProgram> createProgram,CloudVolumeSubmission? submission=null) : IDisposable
{
    private readonly CloudVolumeSubmission submission=submission??new();
    /// <summary>Exposes prepared executable footprints to the enclosing update boundary.</summary>
    internal System.Collections.Generic.IEnumerable<GpuComputeProgram> SubmissionPrograms=>System.Linq.Enumerable.OfType<GpuComputeProgram>(programs);
    private readonly CloudVolumeDensityProgram?[] programs=new CloudVolumeDensityProgram?[14];
    private readonly ulong[] executable=new ulong[14];
    private GpuShaderStorageBuffer? summary;
    private GpuIndirectBuffer? commands;
    private CloudVolumeIdentity? identity;
    private uint frame;
    private bool observed,failed,disposed;
    private PackedUniformBuffer? selectionInputs;
    private GpuFence? fence;
    private DynamicTexture2D? nativeSnapshot;
    private CloudMapInput frozenInput;
    private (float Minimum,float Maximum)? validatedBounds;
    private CloudVolumeFields? generated;
    private uint generatedFrame;
    private bool validating,deferred,generationSkipped;
    internal bool Busy=>fence is not null;
    /// <summary>Identifies retained-domain generation ordered into the caller's final lighting fence.</summary>
    internal bool Chained=>generated is not null&&fence is null;
    internal bool UpdateCompleted {get;private set;}
    internal bool Ready { get; private set; }
    internal string? Failure { get; private set; }

    #region Public API
    #region Preparation and generation
    /// <summary>Submits one bounded density step, freezing native input before deferred validation.</summary>
    internal void Submit(CloudVolumeStorage storage,CloudMapInput input,GpuTexture map,CloudVolumeEncoding encoding,
        Vector3 fallback,Func<bool> current,bool chainRetained=false)
    {
        if(Busy||Chained)throw new InvalidOperationException("Density work is already pending.");
        if(failed)throw new InvalidOperationException(Failure);
        if(!current()||!input.Storage.IsSupported||map.TextureId!=input.Storage.Map||map.Width!=input.Storage.Width||
            map.Height!=input.Storage.Width||map.InternalFormat!=PixelInternalFormat.Rgba32f||!input.Placement.IsFinite)
            throw new InvalidOperationException("Native cloud input is unavailable for submission.");
        UpdateCompleted=false;Prepare();
        if(validatedBounds is null&&NeedsObservation(storage,input))
        {
            EnsureSnapshot(input.Storage.Width);frozenInput=input;
            BeginValidation(input,map,current);validating=true;fence=GpuFence.Insert();return;
        }
        deferred=true;generated=null;
        try
        {
            // A validated owned snapshot outlives the native borrow. Its source stamps remain the original observation's.
            var selected=validatedBounds is not null?frozenInput:input;
            var snapshot=validatedBounds is not null?nativeSnapshot!:map;
            if(validatedBounds is not null)selected=selected with{Storage=selected.Storage with{Map=snapshot.TextureId}};
            Update(storage,selected,snapshot,encoding,fallback,current);
            if(Failure is not null)throw new InvalidOperationException(Failure);
            // New-domain initialization needs its summary before CPU storage admission. Retained
            // fields already have an admitted domain and can feed lighting through GPU barriers.
            if(!chainRetained||generated is null||!storage.Ready||!ReferenceEquals(generated,storage.Published))
                fence=GpuFence.Insert();
        }
        finally{deferred=false;}
    }
    /// <summary>Polls without waiting; reads status only after all producing commands have completed.</summary>
    internal bool Poll(CloudVolumeStorage storage)
    {
        if(fence is null)return true;
        Check(()=>true);
        var status=fence.Poll();if(status==WaitSyncStatus.TimeoutExpired)return false;
        if(status==WaitSyncStatus.WaitFailed)throw new InvalidOperationException("Cloud density fence failed.");
        fence.Dispose();fence=null;Check(()=>true);
        if(validating){validatedBounds=ReadBounds();validating=false;return true;}
        CompletePending(storage);return true;
    }
    /// <summary>Accepts generation status after its own admission fence or the caller's final chained fence.</summary>
    internal void CompletePending(CloudVolumeStorage storage)
    {
        Check(()=>true);
        if(generated is {} fields)
        {
            if(!generationSkipped)ReadGeneration(fields,generatedFrame);
            if(ReferenceEquals(fields,storage.Pending)&&fields.DensityState.InitialComplete)
                storage.PublishPending(()=>fields.DensityState.InitialComplete,()=>true);
            generated=null;generationSkipped=false;
        }
        Ready=storage.Ready;UpdateCompleted=true;
    }
    /// <summary>Admits structural executables and bounded parameter/status/indirect storage within the fixed allowance.</summary>
    internal void Prepare()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(failed)throw new InvalidOperationException(Failure);
        var caps=GpuSupport.Graphics;
        if(caps.MaxComputeShaderStorageBlocks<5||caps.MaxShaderStorageBufferBindings<5||caps.MaxUniformBlockSize<128||
            caps.MaxComputeWorkGroupInvocations<256||caps.MaxComputeWorkGroupSize.Length<3||
            caps.MaxComputeWorkGroupSize[0]<256||caps.MaxComputeSharedMemorySize<1040||caps.MaxComputeWorkGroupCount.Length<3||caps.MaxComputeWorkGroupCount[0]<4096)
            throw new NotSupportedException("Cloud density generation exceeds context capabilities.");
        selectionInputs??=new PackedUniformBuffer(128);
        for(int i=0;i<programs.Length;i++)
        {
            var p=programs[i]??=createProgram(i,i==2||i>=6?selectionInputs:null);
            if(!p.EnsureReady())throw new InvalidOperationException(p.PreparationLog);
            ulong next=((IGpuProgram)p).ExecutableRevision;
            if(observed&&executable[i]!=next)throw new InvalidOperationException("Cloud density executable changed; reset is required.");
            executable[i]=next;
        }
        if(summary is null)
        {summary=GpuShaderStorageBuffer.Create();summary.UploadData(new uint[8]);}
        if(commands is null)
        {commands=GpuIndirectBuffer.Create();commands.UploadData(new uint[]{0,1,1});}
    }

    /// <summary>Consumes only a synchronous certified native borrow, then advances one density budget against owned descriptors.</summary>
    internal bool Update(CloudVolumeStorage storage,CloudMapInput input,GpuTexture map,CloudVolumeEncoding encoding,
        Vector3 fallback,Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(failed)return false;
        if(!current()||!input.Storage.IsSupported||map.TextureId!=input.Storage.Map||map.Width!=input.Storage.Width||
            map.Height!=input.Storage.Width||map.InternalFormat!=PixelInternalFormat.Rgba32f||!input.Placement.IsFinite)
        {Ready=false;return false;}
        var source=new CloudVolumeIdentity(input.Owner,input.World,input.Shader);
        if(identity is not null&&!identity.Matches(source)){storage.Reset();observed=false;}
        if(observed&&input.Frame==frame)return Ready;
        Ready=false;
        try
        {
            Prepare();
            if(observed&&input.Frame<frame){storage.Reset();observed=false;}
            CloudVolumeFields? fields=storage.Pending;
            if(fields is null&&storage.Ready&&!storage.GeometryReplacement)
            {
                var retained=storage.Published;var stamp=retained.DensityState;
                if(stamp.SnapshotValid&&stamp.NativeGeneration==input.Generation&&stamp.NativeUpload==input.Upload&&
                    stamp.NativeAllocation==input.Allocation&&stamp.WindowX==(long)input.Placement.CenterX-input.Placement.WindTileX&&
                    stamp.WindowZ==(long)input.Placement.CenterZ-input.Placement.WindTileZ)fields=retained;
            }
            // Frozen incoming snapshots finish before observing newer weather, even if its height band changed.
            if(fields is null||!fields.DensityState.SnapshotValid)
            {
                var bounds=ValidateInput(input,map,current);
                var previous=storage.Ready?storage.Published.Domain:null;
                var domain=new CloudVolumeDomain(input.Storage.Width,input.Placement,bounds.Minimum,bounds.Maximum,previous);
                float y=(float)domain.Bottom;
                if(!float.IsFinite(y)||MathF.BitIncrement(y)-y>domain.VerticalCell/4)
                    throw new NotSupportedException("Native heights cannot represent the selected density-cell precision.");
                storage.Request(domain,source,encoding,fallback,current);
                fields=storage.Pending??storage.Published;
                Observe(fields,input,map,current);
            }
            if(storage.Pending is not null&&!fields.Initialized && !storage.InitializeNext(current))
            {identity=source;frame=input.Frame;observed=true;Ready=storage.Ready;return Ready;}
            Generate(fields,input.Frame,current);
            if(ReferenceEquals(fields,storage.Pending)&&fields.DensityState.InitialComplete)
                storage.PublishPending(()=>fields.DensityState.InitialComplete,current);
            identity=source;frame=input.Frame;observed=true;Ready=storage.Ready;return Ready;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
        finally
        {
            // Generated bindings may retain owned snapshots, but never a previous-frame native map borrow.
            foreach(var p in programs)if(p is not null)p.Map=default;
        }
    }
    #endregion

    #region Lifetime
    /// <summary>Releases executable/status resources at explicit recovery boundaries without owning volume fields.</summary>
    internal void Reset()
    {
        fence?.Dispose();fence=null;nativeSnapshot?.Dispose();nativeSnapshot=null;
        validating=deferred=generationSkipped=UpdateCompleted=false;validatedBounds=null;generated=null;frozenInput=default;
        Ready=observed=failed=false;Failure=null;identity=null;frame=0;
        Action[] release=new Action[17];
        for(int i=0;i<14;i++){int slot=i;release[i]=()=>{programs[slot]?.Dispose();programs[slot]=null;executable[slot]=0;};}
        release[14]=()=>{summary?.Dispose();summary=null;};release[15]=()=>{commands?.Dispose();commands=null;};
        release[16]=()=>{selectionInputs?.Dispose();selectionInputs=null;};
        RenderingCleanup.Run(release);
    }
    /// <summary>Retires the coordinator once; native textures remain with their producer.</summary>
    public void Dispose(){if(disposed)return;disposed=true;Reset();}
    #endregion
    #endregion

    #region Private
    #region Observation and work completion
    /// <summary>Reduces only validation and height metadata before choosing or replacing the finite density band.</summary>
    private (float Minimum,float Maximum) ValidateInput(CloudMapInput input,GpuTexture map,Func<bool> current)
    {
        if(validatedBounds is {} cached){validatedBounds=null;return cached;}
        EnsureSnapshot(input.Storage.Width);BeginValidation(input,map,current);return ReadBounds();
    }
    /// <summary>Allocates a small owned raw descriptor snapshot independently of the borrowed native map.</summary>
    private void EnsureSnapshot(int width)
    {
        if(nativeSnapshot?.Width==width)return;
        nativeSnapshot?.Dispose();nativeSnapshot=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);
    }
    /// <summary>Validates and freezes the same native observation in one ordered dispatch.</summary>
    private void BeginValidation(CloudMapInput input,GpuTexture map,Func<bool> current)
    {
        summary!.UploadData(new uint[]{0,uint.MaxValue,0,0,0,0,0,0});
        var domain=new CloudVolumeDomain(input.Storage.Width,input.Placement,0,0);
        programs[0]!.Capture(domain,0,0,new CloudVolumeDensityState(),input.Frame,false);
        programs[0]!.Map=map;programs[0]!.Summary=summary;
        programs[0]!.Descriptors=new(nativeSnapshot,TextureAccess.WriteOnly);
        Check(current);Run(0,()=>programs[0]!.Dispatch((input.Storage.Width*input.Storage.Width+63)/64));Check(current);
        programs[0]!.Map=default;
    }
    /// <summary>Consumes a completed height/error reduction without rereading native storage.</summary>
    private (float Minimum,float Maximum) ReadBounds()
    {
        using var read=summary.MapScope<uint>(0,4,MapBufferAccessMask.MapReadBit);
        if(read.Span[0]!=0)throw new InvalidOperationException("Native cloud extinction or height data is invalid.");
        return read.Span[3]==0?(0,0):(Unordered(read.Span[1]),Unordered(read.Span[2]));
    }

    /// <summary>Updates one owned circular snapshot, retaining pending ages and coalescing dependent block demand.</summary>
    private void Observe(CloudVolumeFields fields,CloudMapInput input,GpuTexture map,Func<bool> current)
    {
        var state=fields.DensityState;
        if(state.Revision==uint.MaxValue)throw new InvalidOperationException("Density revision rollover requires reset.");
        state.Revision++;state.SnapshotFrame=input.Frame;state.MaintenanceRequired=true;
        long x=(long)input.Placement.CenterX-input.Placement.WindTileX,z=(long)input.Placement.CenterZ-input.Placement.WindTileZ;
        Configure(1,fields,x,z,input.Frame,!state.InitialComplete);
        programs[1]!.Map=map;Check(current);
        Run(1,()=>programs[1]!.Dispatch((fields.Domain.Width*fields.Domain.Width+63)/64));Check(current);
        state.WindowX=x;state.WindowZ=z;state.SnapshotValid=true;
        state.NativeGeneration=input.Generation;state.NativeUpload=input.Upload;state.NativeAllocation=input.Allocation;
    }

    /// <summary>Publishes at most 4096 whole blocks, repairs filtered occupancy and commits only successful stamps.</summary>
    private void Generate(CloudVolumeFields fields,uint completedFrame,Func<bool> current)
    {
        var state=fields.DensityState;
        generationSkipped=false;
        if(state.InitialComplete&&!state.MaintenanceRequired)
        {
            // The last fenced summary proved the queue empty and no observation has added demand.
            // Keep the retained-domain lighting chain moving without dispatches or a stale summary read.
            state.Selected=state.Changed=0;
            if(deferred){generated=fields;generatedFrame=completedFrame;generationSkipped=true;}
            return;
        }
        for(int op=2;op<6;op++)Configure(op,fields,state.WindowX,state.WindowZ,completedFrame,!state.InitialComplete);
        Check(current);Select(fields,completedFrame,current);
        Run(3,()=>programs[3]!.DispatchIndirect(commands!));
        Run(4,()=>programs[4]!.Dispatch((fields.Domain.Blocks+63)/64));
        Run(5,()=>programs[5]!.Dispatch((fields.Domain.Blocks+63)/64));Check(current);
        if(deferred){generated=fields;generatedFrame=completedFrame;return;}
        ReadGeneration(fields,completedFrame);
    }
    /// <summary>Uses parallel prefixes and a four-byte age threshold, with exact stable ties and no serial domain scan.</summary>
    private void Select(CloudVolumeFields fields,uint completedFrame,Func<bool> current)
    {
        var state=fields.DensityState;bool initial=!state.InitialComplete;
        int groups=(fields.Domain.Blocks+255)/256;
        // Density consumers finish before lighting reuses these summaries through command-stream barriers.
        if(32768+groups*256>245760||246272+groups>(CloudVolumeDomain.ScratchBytes-4096)/4)
            throw new NotSupportedException("Density selection exceeds fixed scratch.");
        void Stage(int operation,int count,int shift=0)
        {
            Configure(operation,fields,state.WindowX,state.WindowZ,completedFrame,initial,shift);
            Run(operation,()=>programs[operation]!.Dispatch(count));
        }
        Stage(2,groups);Stage(6,groups);Stage(8,1);Stage(7,groups);
        if(!initial)
        {
            for(int shift=24;shift>=0;shift-=8){Stage(9,groups,shift);Stage(10,1,shift);}
            Stage(11,groups);Stage(12,groups);
        }
        Stage(13,1);Check(current);
    }
    /// <summary>Commits density counters only once the producer fence has signaled.</summary>
    private void ReadGeneration(CloudVolumeFields fields,uint completedFrame)
    {
        var state=fields.DensityState;
        using var read=summary!.MapScope<uint>(0,8,MapBufferAccessMask.MapReadBit);
        if(read.Span[0]!=0)throw new InvalidOperationException("Generated cloud density cannot be represented as finite R16F.");
        state.Selected=checked((int)read.Span[5]);state.Remaining=checked((int)read.Span[4]);
        state.Changed=checked((int)read.Span[7]);state.MaintenanceRequired=state.Remaining!=0;
        state.LayoutChanged=false;
        if(state.Selected>0)state.CompletedFrame=completedFrame;
        state.InitialComplete|=state.Remaining==0;
    }
    #endregion

    #region Submission and identity
    /// <summary>Tests snapshot stamps without issuing GPU reads or retaining a native texture wrapper.</summary>
    private bool NeedsObservation(CloudVolumeStorage storage,CloudMapInput input)
    {
        if(identity is not null&&!identity.Matches(new(input.Owner,input.World,input.Shader))||observed&&input.Frame<frame)return true;
        if(storage.Pending is {} pending)return !pending.DensityState.SnapshotValid;
        if(!storage.Ready||storage.GeometryReplacement)return true;
        var stamp=storage.Published.DensityState;
        return !stamp.SnapshotValid||stamp.NativeGeneration!=input.Generation||stamp.NativeUpload!=input.Upload||
            stamp.NativeAllocation!=input.Allocation||stamp.WindowX!=(long)input.Placement.CenterX-input.Placement.WindTileX||
            stamp.WindowZ!=(long)input.Placement.CenterZ-input.Placement.WindTileZ;
    }
    /// <summary>Captures all resources through the selected typed operation; unused bindings are compiled away.</summary>
    private void Configure(int op,CloudVolumeFields fields,long x,long z,uint completedFrame,bool initial,int shift=0)
    {
        var p=programs[op]!;p.Capture(fields.Domain,x,z,fields.DensityState,completedFrame,initial,shift);
        p.Descriptors=new(fields.Descriptors,op==1?TextureAccess.ReadWrite:TextureAccess.ReadOnly);
        p.Density=new(fields.Density,op==3?TextureAccess.ReadWrite:TextureAccess.WriteOnly,Layered:true);p.StoredDensity=fields.Density;
        p.Regions=fields.Regions;p.Occupancy=fields.Occupancy;p.Scratch=fields.Scratch;p.Summary=summary;
        p.Commands=new(commands,0,12);
    }

    /// <summary>Uses existing engine authority and makes image, metadata, command and sampler writes visible.</summary>
    private void Run(int op,Action dispatch)
    {
        submission.Run(programs[op]!,dispatch);
    }
    /// <summary>Rejects changed native certificates before and after synchronous submission.</summary>
    private void Check(Func<bool> current)
    {
        if(!current())throw new InvalidOperationException("Native cloud publication changed during density work.");
        var context=RenderContextRegistry.Current();
        for(int i=0;i<programs.Length;i++)
            if(programs[i] is not {IsValid:true} p||((IGpuProgram)p).ExecutableRevision!=executable[i]||
                ((IGpuProgram)p).ExecutableContext!=context)
                throw new InvalidOperationException("Cloud density executable or context changed during work.");
    }
    /// <summary>Decodes the monotonic unsigned float envelope reduction, including negative heights.</summary>
    private static float Unordered(uint bits)=>BitConverter.Int32BitsToSingle(unchecked((int)((bits&0x80000000u)!=0?bits^0x80000000u:~bits)));
    #endregion
    #endregion
}
