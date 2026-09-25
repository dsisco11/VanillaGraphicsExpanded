using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Opt-in matched routing measurements; numerical acceptance never depends on elapsed time.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","Measurement")]
public sealed class WorldProbeRoutingMeasurementTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Reproducible experiment
    /// <summary>Records independent ABBA blocks on supported and selectively unsupported versions of the same fixed room.</summary>
    [Fact]
    public void MatchedFlagOffOnWorkloads()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_ROUTING_MEASUREMENTS")=="1","Explicit measurement run required.");
        EnsureContextValid();
        using var platform=new EngineShaderPlatformScope();
        var rows=new List<Measurement>();
        foreach(bool mixed in new[]{false,true})
        {
            using var room=new SurfaceLightingEnclosureFixture();room.Seed();
            if(mixed)
            {
                room.Geometry.Sample=(x,y,z)=>{var voxel=room.Sample(x,y,z);return x==7&&(voxel.Geometry&3)==2?voxel with {Geometry=(voxel.Geometry&~3u)|3u}:voxel;};
                room.Geometry.Dirty();room.Geometry.Publish();
            }
            var world=new ControlledVoxelWorld {MapSizeY=256};
            world.AddRoom((0,32,0),(7,39,7),materialId:room.BlockId);
            using var assets=new BinaryShaderApiFixture();
            foreach(int batchSize in new[]{1,8})
            {
            using var atlas=new SurfaceLightingWorldProbeFixture(batchSize==1?1:2);
            float[]? reference=null;
            for(int block=0;block<3;block++) foreach(bool enabled in new[]{false,true,true,false})
            {
                var scene=new MeasuredScene(new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),false));
                using var router=new LumOnWorldProbeTraceRouter(enabled,new LumOnWorldProbeTraceService(scene,2048,(_,_)=>true),
                    new LumOnWorldProbeGpuTraceBackend(assets.Api,256,()=>room.Geometry.Scene,()=>room.Snapshot,(_,_)=>true,scene,_=>true));
                using var queries=new SurfaceLightingQueryBatch(assets.Api);
                using var traceTimer=GpuTimerQuery.Create();using var cacheTimer=GpuTimerQuery.Create();using var commitTimer=GpuTimerQuery.Create();
                var timers=new[]{traceTimer,cacheTimer,commitTimer};
                atlas.Resources.ClearAll();
                for(int warm=0;warm<8;warm++) Execute(room,router,queries,atlas,timers,warm,batchSize);
                long callsBefore=scene.Calls,ticksBefore=scene.Ticks;
                using var process=Process.GetCurrentProcess();
                TimeSpan cpuBefore=process.TotalProcessorTime;
                var watch=Stopwatch.StartNew();
                double gpuTrace=0,gpuCache=0,gpuCommit=0,render=0,latency=0;
                int repeats=128/batchSize;
                for(int iteration=0;iteration<repeats;iteration++)
                {
                    var sample=Execute(room,router,queries,atlas,timers,iteration+8,batchSize);
                    gpuTrace+=sample.Trace;gpuCache+=sample.Cache;gpuCommit+=sample.Commit;render+=sample.Render;latency+=sample.Latency;
                }
                watch.Stop();process.Refresh();
                double cpu=(process.TotalProcessorTime-cpuBefore).TotalMilliseconds;
                var pixels=atlas.Resources.ProbeRadianceAtlas.ReadPixels()
                    .Concat(atlas.Resources.ProbeMeta0.ReadPixels()).Concat(atlas.Resources.ProbeDist0.ReadPixels()).Concat(atlas.Resources.ProbeVis0.ReadPixels()).ToArray();
                Assert.True(pixels[0]>.01f,"Measured publication must contain the seeded nonzero surface lighting.");
                reference??=pixels;
                for(int i=0;i<pixels.Length;i++) Assert.InRange(Math.Abs(pixels[i]-reference[i]),0,.003f);
                Assert.Empty(world.LightQueries);
                long calls=scene.Calls-callsBefore;
                int probes=repeats*batchSize;
                if(!enabled)Assert.Equal(probes<<6,calls);
                else if(mixed)Assert.InRange(calls,1,(probes<<6)-1);else Assert.Equal(0,calls);
                rows.Add(new(mixed?"mixed-fallback":"supported",batchSize,block,enabled,probes,probes<<6,calls,
                    gpuTrace,gpuCache,gpuCommit,cpu,(scene.Ticks-ticksBefore)*1000d/Stopwatch.Frequency,render,latency/repeats,watch.Elapsed.TotalMilliseconds));
            }
            }
        }
        string path=Environment.GetEnvironmentVariable("VGE_ROUTING_MEASUREMENT_OUTPUT")??Path.Combine(Path.GetTempPath(),"world-probe-routing-measurements.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,JsonSerializer.Serialize(new{Utc=DateTime.UtcNow,Vendor=GL.GetString(StringName.Vendor),Renderer=GL.GetString(StringName.Renderer),Version=GL.GetString(StringName.Version),Runtime=Environment.Version.ToString(),Os=Environment.OSVersion.ToString(),Cpu=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),Processors=Environment.ProcessorCount,Rows=rows},new JsonSerializerOptions{WriteIndented=true}));
    }
    #endregion

    #region Timed pipeline
    /// <summary>Completes one matched admission batch; GPU intervals contain submissions only, excluding asynchronous polling waits.</summary>
    private static Sample Execute(SurfaceLightingEnclosureFixture room,LumOnWorldProbeTraceRouter router,SurfaceLightingQueryBatch queries,
        SurfaceLightingWorldProbeFixture atlas,GpuTimerQuery[] timers,int frame,int batchSize)
    {
        var item=new LumOnWorldProbeTraceWorkItem(frame,new(0,new(),new(),0,Ticket:11),new(3.5,35.5,3.5),16,8,64,false,.25f,-1,1e-6f,0,true,room.DependencyRevision);
        var elapsed=Stopwatch.StartNew();double render=0;
        var results=new LumOnWorldProbeTraceResult[batchSize];int count=0;
        try
        {
        long start=Stopwatch.GetTimestamp();router.BeginFrame(frame);
        for(int probe=0;probe<batchSize;probe++)Assert.True(router.TryEnqueue(item with {Request=item.Request with {StorageLinearIndex=probe,StorageIndex=new(probe&1,(probe>>1)&1,probe>>2),Ticket=11+probe}}));
        timers[0].Begin();bool ready=router.TryDequeueResult(out var result);timers[0].End();
        render+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        while(count<batchSize)
        {
            if(ready){Assert.True(result.Success);Assert.Equal(64,result.AtlasSamples.Length);results[count++]=result;if(count==batchSize)break;}
            Assert.True(elapsed.Elapsed<TimeSpan.FromSeconds(10),"Trace completion timed out.");
            Thread.Yield();start=Stopwatch.GetTimestamp();ready=router.TryDequeueResult(out result);render+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        var hits=results.SelectMany(value=>value.AtlasSamples).Where(sample=>sample.SurfaceHit.HasValue).Select(sample=>sample.SurfaceHit!.Value).ToArray();
        double cache=0;
        if(hits.Length>0)
        {
            start=Stopwatch.GetTimestamp();timers[1].Begin();queries.Submit(room.Geometry.Scene,room.Snapshot,hits);timers[1].End();render+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            SurfaceLightingQuery[] answers;
            while(true)
            {
                start=Stopwatch.GetTimestamp();bool complete=queries.TryRead(out answers);render+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if(complete)break;
                Assert.True(elapsed.Elapsed<TimeSpan.FromSeconds(10),"Cache completion timed out.");Thread.Yield();
            }
            int index=0;
            for(int probe=0;probe<results.Length;probe++){results[probe]=WorldProbeSurfaceLighting.Resolve(results[probe],answers,ref index);Assert.True(results[probe].Success);Assert.Empty(results[probe].RetrySamples);}
            cache=timers[1].GetResultNanoseconds()/1e6;
        }
        start=Stopwatch.GetTimestamp();timers[2].Begin();Assert.Equal(batchSize,atlas.TryUpload(results,65536));timers[2].End();render+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        double commit=timers[2].GetResultNanoseconds()/1e6,trace=timers[0].GetResultNanoseconds()/1e6;
        elapsed.Stop();
        return new(trace,cache,commit,render,elapsed.Elapsed.TotalMilliseconds);
        }
        finally {foreach(var completed in results)completed.GpuLease?.Dispose();}
    }
    #endregion

    #region Measurement records
    /// <summary>Stores one completed admission's separately attributed intervals in milliseconds.</summary>
    private readonly record struct Sample(double Trace,double Cache,double Commit,double Render,double Latency);
    /// <summary>Stores raw matched block totals; CPU process consumption includes the driver and worker.</summary>
    private sealed record Measurement(string Scenario,int BatchSize,int Block,bool GpuEnabled,int Probes,int PrimaryRays,long CpuTraceCalls,
        double GpuTraceMs,double GpuCacheMs,double GpuCommitMs,double ProcessCpuMs,double TraversalElapsedMs,double RenderCallsElapsedMs,double MeanCompletionLatencyMs,double BlockWallMs);
    /// <summary>Counts actual CPU traversal invocations and their active-call elapsed duration without changing outcomes.</summary>
    private sealed class MeasuredScene(IWorldProbeTraceScene inner):IWorldProbeTraceScene
    {
        public long Calls, Ticks;
        /// <summary>Measures the production collision tracer; no vanilla lighting or synthetic ray outcomes are introduced.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d origin,Vector3 direction,double distance,CancellationToken token,out LumOnWorldProbeTraceHit hit)
        {
            long start=Stopwatch.GetTimestamp();
            try{return inner.Trace(origin,direction,distance,token,out hit);}
            finally{Interlocked.Increment(ref Calls);Interlocked.Add(ref Ticks,Stopwatch.GetTimestamp()-start);}
        }
    }
    #endregion
}
