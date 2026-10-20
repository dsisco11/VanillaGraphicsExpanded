using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Records a bounded authored compute workload without enforcing speed or approximation quality.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeComputeWorkTests(HeadlessGLFixture fixture,Xunit.ITestOutputHelper output):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Separates maximum metadata selection from fixed nonempty direct and diffuse transport.</summary>
    [Fact]
    public void RecordMatchedComputeWork()
    {
        EnsureShaderTestAvailable();
        output.WriteLine($"GPU: {fixture.GLRenderer}. Standalone headless context; the game remains running and GPU contention is uncontrolled. Registers, spills, occupancy, eligible warps and stall counters unavailable through this timer fixture.");
        MeasureDensitySelection();
        MeasureDiffuseRepair();
        var domain=new CloudVolumeDomain(20,default,-100,100);
        using var fields=new CloudVolumeFields(domain,new(Vector3.One));
        fields.Density.UploadDataImmediate(Enumerable.Repeat(.005f,domain.Voxels).ToArray(),0,0,0,domain.Size,66,domain.Size);
        fields.Direct.UploadDataImmediate(Enumerable.Repeat(1f,domain.Voxels*2).ToArray(),0,0,0,domain.Size,66,domain.Size);
        fields.Diffuse.UploadDataImmediate(new float[domain.Voxels*4],0,0,0,domain.Size,66,domain.Size);
        fields.Occupancy.UploadData(Enumerable.Repeat(1u,domain.Blocks).ToArray());
        var header=new uint[(CloudVolumeDomain.ScratchBytes-4096)/4];
        var regions=Enumerable.Range(0,domain.Blocks).Where(r=>r%domain.BlocksX>0&&r%domain.BlocksX<domain.BlocksX-1&&
            r/(domain.BlocksX*domain.BlocksY)>0&&r/(domain.BlocksX*domain.BlocksY)<domain.BlocksX-1&&
            (r/domain.BlocksX)%domain.BlocksY>=4&&(r/domain.BlocksX)%domain.BlocksY<12).Take(32).ToArray();
        Assert.Equal(32,regions.Length);
        for(int i=0;i<32;i++){header[16384+i*2]=(uint)(regions[i]*2);header[16385+i*2]=(uint)(regions[i]*2+1);header[16448+i]=(uint)regions[i];header[16512+i]=header[16576+i]=(uint)regions[i];header[16640+i]=(uint)regions[i];}
        header[38]=32;
        header[16]=64;header[17]=32;fields.Scratch.UploadData(header);
        using var commands=GpuIndirectBuffer.Create();commands.UploadData(new uint[]{64,1,1,32,8,1,32,1,1,32,1,1});
        var light=new AtmosphereLighting(Vector3.UnitY,Vector3.One,Vector3.One,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
            {Moon=Vector3.UnitX,Lunar=Vector3.One};
        var batch=new CloudVolumeWorkBatch(fields,light,1,1,0,0,0,3,0,true,true,commands);
        using var solar=CloudVolumeDirectProgram.Create(Programs.Api,1);using var lunar=CloudVolumeDirectProgram.Create(Programs.Api,2);
        solar.Capture(domain,light.Sun,0);lunar.Capture(domain,light.Moon,0);
        foreach(var p in new[]{solar,lunar}){p.Density=fields.Density;p.Occupancy=fields.Occupancy;p.Scratch=fields.Scratch;p.Direct=new(fields.Direct,TextureAccess.ReadWrite,Layered:true);}
        using var diffuse=CloudVolumeDiffuseProgram.Create(Programs.Api,true);diffuse.Capture(batch,fields.Encoding);
        diffuse.Density=fields.Density;diffuse.Direct=fields.Direct;diffuse.Source=fields.Diffuse;diffuse.Occupancy=fields.Occupancy;
        diffuse.Scratch=fields.Scratch;diffuse.Destination=new(fields.DiffuseSpare,TextureAccess.WriteOnly,Layered:true);
        // Typed dispatches isolate transport from selection, copy, completion and CPU status polling.
        Measure("Direct transport (256 lanes, 64 source-matched groups)",()=>{solar.DispatchIndirect(commands,24);Barrier();lunar.DispatchIndirect(commands,36);});
        Measure("Diffuse transport (256 lanes, 256 cooperative groups)",()=>diffuse.DispatchIndirect(commands,12));
        using var copy=CloudVolumeCopyProgram.Create(Programs.Api);copy.Capture(domain,fields.Encoding,fields.Encoding);
        copy.Scratch=fields.Scratch;copy.Source=fields.Diffuse;copy.Destination=new(fields.DiffuseSpare,TextureAccess.WriteOnly,Layered:true);
        Measure("Diffuse full-field copy",()=>copy.Dispatch(domain.BlocksX,domain.BlocksY,domain.BlocksX));
        Measure("Combined selected transport and coherent copy",()=>{solar.DispatchIndirect(commands,24);Barrier();lunar.DispatchIndirect(commands,36);Barrier();copy.Dispatch(domain.BlocksX,domain.BlocksY,domain.BlocksX);Barrier();diffuse.DispatchIndirect(commands,12);});
        // Keep resources, shader inputs and work identical while changing only boundary ownership.
        var submission=new CloudVolumeSubmission();
        /// <summary>Submits identical transport and copy producers through the selected boundary policy.</summary>
        void Ordered()
        {
            submission.Run(solar,()=>solar.DispatchIndirect(commands,24));
            submission.Run(lunar,()=>lunar.DispatchIndirect(commands,36));
            submission.Run(copy,()=>copy.Dispatch(domain.BlocksX,domain.BlocksY,domain.BlocksX));
            submission.Run(diffuse,()=>diffuse.DispatchIndirect(commands,12));
        }
        foreach(bool batched in new[]{false,true})
        {
            int boundaries=submission.Boundaries,barriers=submission.Barriers;
            long binds=StateCache.Current.ResourceSlotBindCount,textures=StateCache.Current.TextureBindCount;
            Measure(batched?"Batched transport/copy boundary":"Standalone transport/copy boundaries",()=>
            {
                if(batched)submission.Batch(new GpuComputeProgram[]{solar,lunar,copy,diffuse},Ordered);
                else Ordered();
            });
            output.WriteLine($"Matched four-dispatch submission ({(batched?"batched":"standalone")}): boundaries={(submission.Boundaries-boundaries)/4}, barriers={(submission.Barriers-barriers)/4}, image/indexed binds={(StateCache.Current.ResourceSlotBindCount-binds)/4.0}, texture binds={(StateCache.Current.TextureBindCount-textures)/4.0} per sample; program dispatch scopes unchanged.");
        }
        var dirty=new uint[domain.Blocks*16];foreach(int r in regions)dirty[r*16]=8192|16384;
        using var publication=new CloudVolumePublication(()=>CloudVolumePublishProgram.Create(Programs.Api));publication.Publish(fields,0,domain.Bytes);
        Measure("Dirty publication (32 direct/diffuse blocks)",()=>publication.Publish(fields,0,domain.Bytes),()=>fields.Regions.UploadData(dirty));
        Measure("Combined transport, coherent copy and dirty publication",()=>{solar.DispatchIndirect(commands,24);Barrier();lunar.DispatchIndirect(commands,36);Barrier();copy.Dispatch(domain.BlocksX,domain.BlocksY,domain.BlocksX);Barrier();diffuse.DispatchIndirect(commands,12);Barrier();publication.Publish(fields,0,domain.Bytes);},()=>fields.Regions.UploadData(dirty));
        copy.Capture(domain,fields.Encoding,fields.Encoding,repair:true);
        Measure("Bounded prior-block repair (32 complete blocks)",()=>copy.Dispatch(32));
        Measure("Combined transport, bounded repair and dirty publication",()=>{solar.DispatchIndirect(commands,24);Barrier();lunar.DispatchIndirect(commands,36);Barrier();copy.Dispatch(32);Barrier();diffuse.DispatchIndirect(commands,12);Barrier();publication.Publish(fields,0,domain.Bytes);},()=>fields.Regions.UploadData(dirty));
        output.WriteLine($"Scene: width=20, guarded {domain.Size}x66x{domain.Size}, extinction=.005, all occupancy set, old diffuse=0, unit sources/environment. Direct 64 block-source jobs, 32 per source, no source-mismatched groups, 16384 live occupied voxel integrals. Diffuse 32 blocks, 8192 occupied interior voxels, 65536 gather rays. Density budget=4096, direct=64, diffuse=32. Authored selected blocks have no guard, zero-density or edge lane exits. Publication bracket is a separate unchanged-path cost; baseline excluded it.");
        using var status=fields.Scratch.MapScope<uint>(25*4,1,MapBufferAccessMask.MapReadBit);Assert.Equal(0u,status.Span[0]);
        MeasureCompleteUpdates();
    }
    #endregion
    #region Private
    /// <summary>Compares maximum-domain bandwidth with bounded repair using the same source, destination and timer bracket.</summary>
    private void MeasureDiffuseRepair()
    {
        var domain=new CloudVolumeDomain(200,default,-100,100);var encoding=new CloudVolumeEncoding(Vector3.One);
        using var source=DynamicTexture3D.Create(domain.Size,66,domain.Size,PixelInternalFormat.Rgba16f,textureTarget:TextureTarget.Texture3D);
        using var target=DynamicTexture3D.Create(domain.Size,66,domain.Size,PixelInternalFormat.Rgba16f,textureTarget:TextureTarget.Texture3D);
        source.Clear(.25f,.5f,.75f);target.Clear();
        using var scratch=GpuShaderStorageBuffer.Create();var header=new uint[16672];
        header[38]=32;for(int i=0;i<32;i++)header[16640+i]=(uint)(1+i+domain.BlocksX*(4+domain.BlocksY));
        scratch.UploadData(header);
        using var copy=CloudVolumeCopyProgram.Create(Programs.Api);copy.Scratch=scratch;copy.Source=source;
        copy.Destination=new(target,TextureAccess.WriteOnly,Layered:true);copy.Capture(domain,encoding,encoding);
        Measure("Maximum-domain full diffuse copy",()=>copy.Dispatch(domain.BlocksX,domain.BlocksY,domain.BlocksX));
        copy.Capture(domain,encoding,encoding,repair:true);
        Measure("Maximum-domain bounded repair (32 complete interior blocks)",()=>copy.Dispatch(32));
        output.WriteLine($"Matched copy scene: {domain.Size}x66x{domain.Size}, same-scale RGBA16F fields; full read/write payload={domain.CopyBytes} bytes; repair payload={32L*256*16} bytes plus bounded list/counter traffic. Includes dispatch and required visibility barriers; allocation/clears outside timing. Retained-list GPU completion and drawable publication are included in the separate complete-update sequence, not this isolated bandwidth bracket.");
    }
    /// <summary>Times every real deferred update callback through publication, excluding view draws and simulated frame waits.</summary>
    private void MeasureCompleteUpdates()
    {
        using var map=DynamicTexture2D.Create(20,20,PixelInternalFormat.Rgba32f);
        using var camera=TestFrameCamera.CreateFromProjection([1,0,0,0,0,1,0,0,0,0,-1.00020002f,-1,0,0,-.200020002f,0],1,1,.1f,1000);
        using var solution=new CloudVolumeSolution(Programs.Api,true);
        var settings=CloudVolumeViewSettings.Default;solution.Prepare(settings);
        var lighting=new AtmosphereLighting(Vector3.UnitY,Vector3.One,Vector3.One,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
            {Moon=Vector3.UnitX,Lunar=Vector3.One};
        var input=new CloudMapInput(new object(),1,1,1,1,1,0,new(map.TextureId,2,3,4,20,50,5),default);
        var native=new float[1600];for(int i=0;i<400;i++){native[i*4]=.25f;native[i*4+2]=-2;native[i*4+3]=2;}
        map.UploadDataImmediate(native);
        using var timer=GpuTimerQuery.Create("Tests.Cloud.CompleteUpdate");uint frame=0;
        // Startup allocation, initialization and first publication warm the complete owning path outside the reported samples.
        Complete(false);
        foreach(string scenario in new[]{"unchanged","wind only","same-content upload","evolving","source change","empty occupancy","disabled lighting jobs"})
        {
            var samples=new List<double>();var cpuSamples=new List<double>();var callbacks=new List<int>();
            for(int sample=0;sample<3;sample++)
            {
                if(scenario is "evolving" or "empty occupancy")
                {
                    for(int i=0;i<400;i++)native[i*4]=scenario=="empty occupancy"?0:sample%2==0?.2505f:.25f;
                    map.UploadDataImmediate(native);input=input with{Upload=input.Upload+1};
                }
                if(scenario=="source change")lighting=lighting with{Sun=sample%2==0?Vector3.UnitX:Vector3.UnitY,Lunar=sample%2==0?Vector3.Zero:Vector3.One};
                if(scenario=="wind only")input=input with{Placement=input.Placement with{Offset=new Vector3(sample+1,0,sample+2),WindX=(sample+1)*.1}};
                if(scenario=="same-content upload"){map.UploadDataImmediate(native);input=input with{Upload=input.Upload+1};}
                if(scenario=="disabled lighting jobs")settings=settings with{SelfShadowing=false,Diffuse=false};
                var measured=Complete(true);samples.Add(measured.Milliseconds);cpuSamples.Add(measured.CpuMilliseconds);callbacks.Add(measured.Callbacks);
                var fields=solution.Storage.Published;
                uint[] header,records,occupied;
                // Diagnostic maps occur after the test fence and outside every reported timer interval.
                using(var read=fields.Scratch.MapScope<uint>(0,32,MapBufferAccessMask.MapReadBit))header=read.Span.ToArray();
                using(var read=fields.Regions.MapScope<uint>(0,fields.Domain.Blocks*16,MapBufferAccessMask.MapReadBit))records=read.Span.ToArray();
                using(var read=fields.Occupancy.MapScope<uint>(0,fields.Domain.Blocks,MapBufferAccessMask.MapReadBit))occupied=read.Span.ToArray();
                uint directAge=0,diffuseAge=0,completionAge=0;
                for(int r=0;r<fields.Domain.Blocks;r++)
                {
                    if((occupied[r]&1)==0)continue;
                    int b=r*16;
                    bool pendingDirect=settings.SelfShadowing&&(records[b+5]!=records[b+6]||lighting.Lunar!=Vector3.Zero&&records[b+7]!=records[b+8]);
                    if(pendingDirect)directAge=Math.Max(directAge,frame-records[b+9]);
                    if(settings.Diffuse&&(records[b]&4)!=0)diffuseAge=Math.Max(diffuseAge,frame-records[b+14]);
                    foreach(int offset in new[]{10,11,15})if(records[b+offset]!=0)completionAge=Math.Max(completionAge,frame-records[b+offset]);
                }
                output.WriteLine($"Sequence {scenario}[{sample}]: callbacks={measured.Callbacks}, simulated frame intervals={measured.Callbacks-1}, GPU={measured.Milliseconds:F4}ms, CPU Poll/Submit={measured.CpuMilliseconds:F4}ms, occupied blocks={occupied.Count(v=>(v&1)!=0)}/{fields.Domain.Blocks}, density selected/changed/backlog={fields.DensityState.Selected}/{fields.DensityState.Changed}/{fields.DensityState.Remaining}, useful selected direct/diffuse jobs={header[16]}/{header[17]}, selection backlogs={header[22]}/{header[23]}, pending ages direct/diffuse={directAge}/{diffuseAge}, oldest stamped completion age={completionAge}, normalized jobs per simulated callback={header[16]/(double)measured.Callbacks:F2}/{header[17]/(double)measured.Callbacks:F2}, diffuse copy bytes={solution.Diffuse.LastCopyBytes}.");
            }
            var ordered=samples.Order().ToArray();
            output.WriteLine($"Complete deferred update {scenario}: median {ordered[1]:F4} ms GPU, {cpuSamples.Order().ElementAt(1):F4} ms CPU Poll/Submit; summed callback samples {string.Join(", ",samples.Select(t=>t.ToString("F4")))} ms; callbacks {string.Join(", ",callbacks)}.");
        }
        output.WriteLine("Actual CloudVolumeSolution.Poll/Submit, width20, default detail/shadows/diffuse until separately labelled disabled-job case; raw extinction .25/.2505 or genuinely empty zero, heights[-2,2] native=>[-100,100] metres, budgets4096/64/32. Includes validation/snapshot, observation, generation/occupancy, selection, direct/diffuse completion, coherent copy and publication as scheduled. Startup/native upload/view draws/test inter-frame waits excluded; CPU submission gaps included. CPU stopwatch measures only Poll/Submit calls, excluding logging, diagnostic maps, timer/fence waits and native upload; this is headless submission cost, not live engine CPU time. Callbacks simulate rendered frames with fences forced complete between callbacks, not actual game latency/FPS. Final publication callback does not submit the next chain in this fixture; normalized job counts are fixture callback ratios, not measured live throughput. Instrumentation adds three metadata maps outside timing; age statistics exclude certified empty blocks. Useful selected jobs denote occupied eligible block jobs, not a count of nonzero contributing voxel rays. Changed density blocks use exact arithmetic comparison without approximation thresholds. Production CPU readbacks are source-audited separately: density summary and combined lighting status/diffuse count after final fence, plus descriptor validation for new observations.");

        /// <summary>Advances one complete retained publication while keeping test waits outside each GPU timer interval.</summary>
        (double Milliseconds,int Callbacks,double CpuMilliseconds) Complete(bool measure)
        {
            uint before=solution.Drawable?.Revision??0;double sum=0,cpu=0;
            for(int callback=0;callback<64;callback++)
            {
                var observation=input with{Frame=++frame};if(measure)timer.Begin();
                long started=System.Diagnostics.Stopwatch.GetTimestamp();solution.Poll(observation);
                cpu+=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                bool published=solution.Drawable is {} drawable&&drawable.Revision>before;
                if(!published)
                {
                    int boundaries=solution.Submission.Boundaries,dispatches=solution.Submission.Dispatches,barriers=solution.Submission.Barriers;
                    long binds=StateCache.Current.ResourceSlotBindCount;
                    started=System.Diagnostics.Stopwatch.GetTimestamp();solution.Submit(observation,map,lighting,camera,settings,()=>true);
                    cpu+=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    int count=solution.Submission.Dispatches-dispatches;
                    Assert.Equal(1,solution.Submission.Boundaries-boundaries);
                    Assert.Equal(count+1,solution.Submission.Barriers-barriers);
                    if(measure)output.WriteLine($"Update submission: dispatches={count}, boundaries=1, barriers={count+1}, actual image/indexed binds={StateCache.Current.ResourceSlotBindCount-binds}; previous per-pass path requires {count} boundaries and {count*2} per-pass barriers, plus one post-refine barrier only when diffuse runs.");
                }
                if(measure)timer.End();
                using var completed=GpuFence.Insert();
                Assert.Contains(completed.Wait(TimeSpan.FromSeconds(1)),new[]{WaitSyncStatus.AlreadySignaled,WaitSyncStatus.ConditionSatisfied});
                if(measure)sum+=timer.GetResultNanoseconds()/1e6;
                Assert.Null(solution.Failure);if(published)return(sum,callback+1,cpu);
            }
            throw new InvalidOperationException("Bounded test update did not produce a completed publication.");
        }
    }
    /// <summary>Measures queue work at maximum coverage using metadata only and resetting identical input before each trial.</summary>
    private void MeasureDensitySelection()
    {
        var domain=new CloudVolumeDomain(200,default,-100,100);var records=new uint[domain.Blocks*16];
        for(int i=0;i<domain.Blocks;i++){records[i*16]=1;records[i*16+1]=2;records[i*16+3]=(uint)(i%997+1);}
        using var regions=GpuShaderStorageBuffer.Create();using var scratch=GpuShaderStorageBuffer.Create();
        using var summary=GpuShaderStorageBuffer.Create();using var commands=GpuShaderStorageBuffer.Create();
        var header=new uint[(CloudVolumeDomain.ScratchBytes-4096)/4];header[5]=(uint)(domain.Blocks-17);
        var programs=new[]{2,6,7,8,9,10,11,12,13}.ToDictionary(op=>op,op=>CloudVolumeDensityProgram.Create(Programs.Api,op,false));
        try
        {
            foreach(var p in programs.Values){p.Regions=regions;p.Scratch=scratch;p.Summary=summary;p.Commands=new(commands,0,12);}
            void Reset(){regions.UploadData(records);scratch.UploadData(header);summary.UploadData(new uint[8]);commands.UploadData(new uint[3]);}
            void Submit(int op,int groups,bool initial,int shift=0)
            {programs[op].Capture(domain,0,0,new CloudVolumeDensityState{Revision=2,SnapshotFrame=1},1000,initial,shift);programs[op].Dispatch(groups);Barrier();}
            void Select(bool initial)
            {
                int groups=(domain.Blocks+255)/256;Submit(2,groups,initial);Submit(6,groups,initial);Submit(8,1,initial);Submit(7,groups,initial);
                if(!initial){foreach(int shift in new[]{24,16,8,0}){Submit(9,groups,false,shift);Submit(10,1,false,shift);}Submit(11,groups,false);Submit(12,groups,false);}
                Submit(13,1,initial);
            }
            header[^1]=0xdeadbeef;
            Measure("Density selection",()=>Select(false),Reset);VerifyDensitySelection(domain,records,header,regions,scratch,summary,commands,false);
            for(int test=0;test<2;test++)
            {
                // Sparse initial fill and scarce steady work exercise age creation, stable ties and empty priority halves.
                Array.Clear(records);
                for(int r=0;r<domain.Blocks;r++)if(r%(test==0?7:113)==0)
                {records[r*16]=r%3==0?16u:1u;records[r*16+1]=r%5==0?0u:2u;records[r*16+2]=77;records[r*16+3]=500;records[r*16+4]=88;}
                header[5]=(uint)(domain.Blocks-17);Reset();Select(test==0);
                VerifyDensitySelection(domain,records,header,regions,scratch,summary,commands,test==0);
            }
            output.WriteLine($"Density selection: {domain.Blocks} pending blocks, steady queue, cyclic cursor={header[5]}, varied retained ages, budget4096; parallel chunks256, four radix bytes. Exact CPU ordering checks cover maximum backlog, sparse initial fill, scarce steady work, cursor wrap and stable ties.");
        }
        finally{foreach(var p in programs.Values)p.Dispose();}
    }
    /// <summary>Records three warmed GPU timer samples without placing a speed requirement on the result.</summary>
    private void Measure(string name,Action dispatch,Action? reset=null)
    {
        using var timer=GpuTimerQuery.Create("Tests.Cloud.ComputeWork");var times=new List<double>();
        for(int i=0;i<4;i++){reset?.Invoke();Barrier();timer.Begin();dispatch();Barrier();timer.End();double ms=timer.GetResultNanoseconds()/1e6;if(i>0)times.Add(ms);}
        times.Sort();output.WriteLine($"{name}: median {times[1]:F4} ms; samples {string.Join(", ",times.Select(t=>t.ToString("F4")))} ms.");
    }
    /// <summary>Checks exact selected sets and retained metadata against independently ordered CPU demand.</summary>
    private static void VerifyDensitySelection(CloudVolumeDomain domain,uint[] records,uint[] header,GpuShaderStorageBuffer regions,
        GpuShaderStorageBuffer scratch,GpuShaderStorageBuffer summary,GpuShaderStorageBuffer commands,bool initial)
    {
        var expected=(uint[])records.Clone();var pending=new List<int>();
        for(int r=0;r<domain.Blocks;r++)
        {
            int b=r*16;uint flags=expected[b];bool waiting=(flags&1)!=0;
            if((flags&16)!=0||waiting&&expected[b+1]==0)
            {if(!waiting||expected[b+1]==0)expected[b+3]=initial?1u:1000u;expected[b+1]=2;flags|=1;}
            expected[b]=flags&~(16u|256u);if((flags&1)!=0)pending.Add(r);
        }
        int cursor=(int)header[5],fairLimit=initial?4096:2048;
        var fair=pending.OrderBy(r=>(r+domain.Blocks-cursor)%domain.Blocks).Take(fairLimit).ToArray();
        var priority=pending.Except(fair).OrderByDescending(r=>unchecked(1000u-expected[r*16+3])).ThenBy(r=>r).Take(4096-fair.Length).ToArray();
        var selected=fair.Concat(priority).ToHashSet();foreach(int r in selected)expected[r*16]|=256;
        // Read back only validation data after the complete measured submission has finished.
        uint[] Read(GpuShaderStorageBuffer b,int first,int count)
        {using var view=b.MapScope<uint>(first*4,count,MapBufferAccessMask.MapReadBit);return view.Span.ToArray();}
        var actual=Read(scratch,64,selected.Count);Assert.Equal(fair.Select(r=>(uint)r),actual.Take(fair.Length));
        Assert.Equal(selected.Order().Select(r=>(uint)r),actual.Order());Assert.Equal(expected,Read(regions,0,records.Length));
        Assert.Equal(new uint[]{(uint)selected.Count,1,1},Read(commands,0,3));Assert.Equal((uint)selected.Count,Read(summary,5,1)[0]);
        Assert.Equal((uint)pending.Count,Read(summary,6,1)[0]);Assert.Equal(fair.Length==0?header[5]:(uint)((fair[^1]+1)%domain.Blocks),Read(scratch,5,1)[0]);
        Assert.Equal(0xdeadbeefu,Read(scratch,header.Length-1,1)[0]);
    }
    /// <summary>Orders typed metadata, image and indirect work between measured dispatches.</summary>
    private static void Barrier()=>GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);
    #endregion
}
