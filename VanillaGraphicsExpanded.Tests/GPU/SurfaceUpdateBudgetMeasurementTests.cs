using System.Diagnostics;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Opt-in matched shader costs and simulated completion latency for separate update budgets.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","Measurement")]
public sealed class SurfaceUpdateBudgetMeasurementTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Matched measurement
    /// <summary>Runs independent ABBA legs with identical useful pages and synchronous completion readback.</summary>
    [Fact]
    public void MatchedLegacyAndSeparateBudgets()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_SURFACE_BUDGET_MEASUREMENTS")=="1","Explicit measurement run required.");
        EnsureContextValid();var rows=new List<object>();
        foreach(bool diagnosticsEnabled in new[]{false,true})
        for(int block=0;block<3;block++)foreach(bool separate in new[]{false,true,true,false})
        {
            using var room=new SurfaceLightingEnclosureFixture(worldHeight:40);
            room.Seed();
            room.Diagnostics.Enabled=diagnosticsEnabled;
            using var seedTimer=GpuTimerQuery.Create();using var directTimer=GpuTimerQuery.Create();using var indirectTimer=GpuTimerQuery.Create();
            var timers=new[]{seedTimer,directTimer,indirectTimer};
            using var seedWork=GpuShaderStorageBuffer.Create();using var directWork=GpuShaderStorageBuffer.Create();using var indirectWork=GpuShaderStorageBuffer.Create();
            var storage=new[]{seedWork,directWork,indirectWork};foreach(var buffer in storage)buffer.EnsureCapacity(8<<4,growExponentially:false);
            // Warm all operation variants before timing; every leg starts with the same captured enclosure.
            foreach(uint operation in new uint[]{0,4,1})for(int warm=0;warm<8;warm++)
            {
                if(operation==0){room.DispatchDiagnosticWork(3,pageCount:8);Assert.Equal(8,room.ReadDiagnosticCompletion(8));}
                room.DispatchDiagnosticWork(operation,pageCount:8,steps:1024);Assert.Equal(8,room.ReadDiagnosticCompletion(8));
            }
            GpuTestFence.WaitForGpuOrSkip("Surface update budget warmup");room.Diagnostics.Poll();
            var stages=new[]{SurfaceWorkStage.Seed,SurfaceWorkStage.Direct,SurfaceWorkStage.Indirect};
            var before=stages.Select(room.Diagnostics.Snapshot).ToArray();
            const int pages=24,repetitions=16;
            double gpu=0,wall=0,maxFrame=0,pollWall=0;int frames=0,completed=0,dispatches=0;
            var stageGpu=new double[3];var stageWall=new double[3];var stagePages=new int[3];
            for(int repetition=0;repetition<repetitions;repetition++)
            {
                // Seed must perform initialization rather than the already-valid early return.
                // Reset and its completion wait are setup, excluded from every measured interval.
                room.DispatchDiagnosticWork(3,pageCount:pages);
                Assert.Equal(pages,room.ReadDiagnosticCompletion(pages));
                GpuTestFence.WaitForGpuOrSkip("Surface update budget reset");room.Diagnostics.Poll();
                int seed=0,direct=0,indirect=0,frame=0;
                while(seed<pages||direct<pages||indirect<pages)
                {
                    double frameWall=0;var pending=new List<(int Stage,int Count,double Submit)>();var measuredStages=new List<int>();
                    // Legacy prioritizes seeding then alternates direct and indirect service.
                    // Separate budgets admit both refresh stages every frame after initial seeding.
                    if(seed<pages)Dispatch(0,0,ref seed,separate?8:4);
                    else if(separate){Dispatch(2,1,ref indirect,4);Dispatch(1,4,ref direct,8);}
                    else if((frame&1)==0)Dispatch(1,4,ref direct,4);
                    else Dispatch(2,1,ref indirect,4);
                    // Independent SSBOs let the new path submit both stages before any blocking status map.
                    foreach(var pendingStage in pending)Collect(pendingStage.Stage,pendingStage.Count,pendingStage.Submit);
                    GpuTestFence.WaitForGpuOrSkip("Surface update budget measurement");
                    long pollStarted=Stopwatch.GetTimestamp();room.Diagnostics.Poll();
                    pollWall+=Stopwatch.GetElapsedTime(pollStarted).TotalMilliseconds;
                    foreach(int stage in measuredStages)
                    {
                        Assert.True(timers[stage].TryGetResultNanoseconds(out long nanos));
                        double gpuTime=nanos/1e6;gpu+=gpuTime;stageGpu[stage]+=gpuTime;
                    }
                    frames++;frame++;maxFrame=Math.Max(maxFrame,frameWall);

                    // Submit a bounded stage batch and optionally defer its completion read.
                    void Dispatch(int stage,uint operation,ref int cursor,int budget)
                    {
                        int count=Math.Min(budget,pages-cursor);if(count==0)return;
                        timers[stage].Begin();long started=Stopwatch.GetTimestamp();
                        room.DispatchDiagnosticWork(operation,count,steps:1024,frame:(uint)frame,firstPage:cursor,workStorage:storage[stage]);
                        timers[stage].End();
                        double submit=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        if(separate)pending.Add((stage,count,submit));else Collect(stage,count,submit);
                        cursor+=count;
                    }

                    // Measure synchronous status readback after the selected submission ordering.
                    void Collect(int stage,int count,double submit)
                    {
                        long started=Stopwatch.GetTimestamp();int ready=room.ReadDiagnosticCompletion(count,storage[stage]);
                        double elapsed=submit+Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        Assert.Equal(count,ready);measuredStages.Add(stage);
                        wall+=elapsed;frameWall+=elapsed;stageWall[stage]+=elapsed;stagePages[stage]+=count;
                        completed+=ready;dispatches++;
                    }
                }
            }
            Assert.Equal(repetitions*pages*3,completed);
            var after=stages.Select(room.Diagnostics.Snapshot).ToArray();
            var counters=after.Select((value,index)=>value.Counters.Select((count,counter)=>count-before[index].Counters[counter]).ToArray()).ToArray();
            if(diagnosticsEnabled)for(int stage=0;stage<3;stage++)
            {
                Assert.Equal(after[stage].Submitted-before[stage].Submitted,after[stage].Collected-before[stage].Collected);
                Assert.Equal(0,after[stage].Skipped-before[stage].Skipped);Assert.Equal(0,after[stage].ReadFailures-before[stage].ReadFailures);
                Assert.Equal((ulong)(repetitions*pages<<6),counters[stage][(int)SurfaceWorkCounter.Texels]);
                Assert.Equal((ulong)(repetitions*pages<<6),counters[stage][(int)SurfaceWorkCounter.Completed]);
                Assert.Equal(0ul,counters[stage][(int)SurfaceWorkCounter.Unchanged]);
            }
            rows.Add(new {Block=block,Separate=separate,DiagnosticsEnabled=diagnosticsEnabled,Repetitions=repetitions,Frames=frames,FramesPerSweep=frames/repetitions,
                SuccessfulPageStatuses=completed,AttemptedTexels=completed<<6,MeasuredCompletedTexels=diagnosticsEnabled?(ulong?)(counters.Sum(values=>(long)values[(int)SurfaceWorkCounter.Completed])):null,
                Dispatches=dispatches,GpuMilliseconds=gpu,DiagnosticPollMilliseconds=pollWall,
                DispatchAndReadbackMilliseconds=wall,MaximumSimulatedFrameMilliseconds=maxFrame,
                StageGpuMilliseconds=stageGpu,StageWallMilliseconds=stageWall,StagePages=stagePages,
                Counters=counters.Select(values=>Enum.GetValues<SurfaceWorkCounter>().Where(value=>value!=SurfaceWorkCounter.Count).ToDictionary(value=>value.ToString(),value=>values[(int)value]))});
        }
        string path=Environment.GetEnvironmentVariable("VGE_SURFACE_BUDGET_MEASUREMENT_OUTPUT")??Path.Combine(Path.GetTempPath(),"surface-update-budget-measurements.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,JsonSerializer.Serialize(new {Utc=DateTime.UtcNow,Vendor=GL.GetString(StringName.Vendor),Renderer=GL.GetString(StringName.Renderer),
            Version=GL.GetString(StringName.Version),Runtime=Environment.Version.ToString(),
            Config=new {Pages=24,Texels=64,Rays=1,Steps=1024,Distance=512,IndependentAbbaBlocks=3,StageOrder=new[]{"seed","direct","indirect"}},Rows=rows},new JsonSerializerOptions{WriteIndented=true}));
    }
    #endregion
}
