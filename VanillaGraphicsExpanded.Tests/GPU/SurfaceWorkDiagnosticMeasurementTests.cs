using System.Diagnostics;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Opt-in matched instrumentation cost measurements with explicit attempted and completed work.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","Measurement")]
public sealed class SurfaceWorkDiagnosticMeasurementTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Matched measurement
    /// <summary>Measures warmed independent ABBA blocks without treating timing as correctness acceptance.</summary>
    [Fact]
    public void MatchedDiagnosticsOffOn()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_SURFACE_DIAGNOSTICS_MEASUREMENTS")=="1","Explicit measurement run required.");
        EnsureContextValid();var rows=new List<object>();
        foreach(var stage in new[]{SurfaceWorkStage.Capture,SurfaceWorkStage.Direct,SurfaceWorkStage.Indirect})
        foreach(bool unresolved in new[]{false,true})
        {
            float[]? reference=null;
            for(int block=0;block<3;block++)foreach(bool enabled in new[]{false,true,true,false})
            {
                // A new fixture gives every leg the same captured data, lighting and empty history.
                using var room=new SurfaceLightingEnclosureFixture(skyFloor:unresolved,worldHeight:unresolved?256:40);
                room.Diagnostics.Enabled=enabled;room.Seed();
                if(unresolved&&stage!=SurfaceWorkStage.Indirect)
                {
                    var domain=room.Geometry.Plan.Surface!.Value;
                    room.Geometry.Move(room.Geometry.Plan with {Surface=new(new(domain.Min.X,34,domain.Min.Z),domain.Max)});
                }
                using var assets=new BinaryShaderApiFixture();
                using var capture=stage==SurfaceWorkStage.Capture?CreateCapture(assets):null;
                var diagnostics=capture?.Diagnostics??room.Diagnostics;diagnostics.Enabled=enabled;
                Action<uint> dispatchWork=stage==SurfaceWorkStage.Capture?_=>room.DispatchDiagnosticCapture(capture!):
                    frame=>room.DispatchDiagnosticWork(stage==SurfaceWorkStage.Direct?4u:1u,frame:frame);
                using var timer=GpuTimerQuery.Create();
                for(int warm=0;warm<8;warm++)Run(diagnostics,dispatchWork,timer,(uint)warm);
                var before=diagnostics.Snapshot(stage);
                double gpu=0,submit=0,poll=0;
                using var process=Process.GetCurrentProcess();var cpuBefore=process.TotalProcessorTime;
                var wall=Stopwatch.StartNew();
                const int dispatches=64;
                for(int dispatch=0;dispatch<dispatches;dispatch++)
                {
                    var value=Run(diagnostics,dispatchWork,timer,(uint)(dispatch+8));
                    gpu+=value.Gpu;submit+=value.Submit;poll+=value.Poll;
                }
                wall.Stop();process.Refresh();
                double processCpu=(process.TotalProcessorTime-cpuBefore).TotalMilliseconds;
                var after=diagnostics.Snapshot(stage);
                var pixels=room.ReadDiagnosticPixels(stage);reference??=pixels;Assert.Equal(reference,pixels);
                var counters=after.Counters.Select((value,index)=>value-before.Counters[index]).ToArray();
                Assert.Equal(dispatches,after.Submitted-before.Submitted);
                if(enabled)
                {
                    Assert.Equal(dispatches,after.Collected-before.Collected);
                    Assert.Equal(0,after.Skipped-before.Skipped);
                    Assert.Equal((ulong)(dispatches<<8),counters[(int)SurfaceWorkCounter.Texels]);
                    Assert.Equal(unresolved?0ul:(ulong)(dispatches<<8),counters[(int)SurfaceWorkCounter.Completed]);
                    Assert.Equal(counters[(int)SurfaceWorkCounter.Rays],
                        counters[(int)SurfaceWorkCounter.Hit]+counters[(int)SurfaceWorkCounter.Sky]+counters[(int)SurfaceWorkCounter.Outside]+
                        counters[(int)SurfaceWorkCounter.Unpublished]+counters[(int)SurfaceWorkCounter.Unsupported]+
                        counters[(int)SurfaceWorkCounter.Budget]+counters[(int)SurfaceWorkCounter.Distance]);
                }
                rows.Add(new {Stage=stage.ToString(),Workload=unresolved?"outside-coverage":"resolved-enclosure",Block=block,Enabled=enabled,
                    Dispatches=dispatches,Pages=dispatches<<2,AttemptedTexels=dispatches<<8,GpuMilliseconds=gpu,
                    SubmitMilliseconds=submit,PollMilliseconds=poll,ProcessCpuMilliseconds=processCpu,WallMilliseconds=wall.Elapsed.TotalMilliseconds,
                    Collected=after.Collected-before.Collected,Skipped=after.Skipped-before.Skipped,
                    MeasuredGpuMilliseconds=after.GpuMilliseconds-before.GpuMilliseconds,
                    CompletionMilliseconds=after.CompletionMilliseconds-before.CompletionMilliseconds,
                    Counters=Enum.GetValues<SurfaceWorkCounter>().Where(value=>value!=SurfaceWorkCounter.Count).ToDictionary(value=>value.ToString(),value=>counters[(int)value])});
            }
        }
        string path=Environment.GetEnvironmentVariable("VGE_SURFACE_DIAGNOSTICS_MEASUREMENT_OUTPUT")??Path.Combine(Path.GetTempPath(),"surface-work-diagnostics-measurements.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,JsonSerializer.Serialize(new {Utc=DateTime.UtcNow,Vendor=GL.GetString(StringName.Vendor),Renderer=GL.GetString(StringName.Renderer),
            Version=GL.GetString(StringName.Version),Runtime=Environment.Version.ToString(),Os=Environment.OSVersion.ToString(),
            Cpu=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),Processors=Environment.ProcessorCount,
            Config=new {PageEdge=8,PagesPerDispatch=4,TexelsPerPage=64,RaysPerTexel=1,Steps=64,WarmupDispatches=8,IndependentAbbaBlocks=3},Rows=rows},
            new JsonSerializerOptions{WriteIndented=true}));
    }
    #endregion

    #region Isolated dispatch timing
    /// <summary>Measures submissions and polling separately; GPU completion waiting occurs outside both CPU intervals.</summary>
    private static (double Gpu,double Submit,double Poll) Run(SurfaceWorkDiagnostics diagnostics,Action<uint> dispatchWork,GpuTimerQuery timer,uint frame)
    {
        timer.Begin();long start=Stopwatch.GetTimestamp();dispatchWork(frame);
        double submit=Stopwatch.GetElapsedTime(start).TotalMilliseconds;timer.End();
        GpuTestFence.WaitForGpuOrSkip("Surface diagnostic measurement");
        Assert.True(timer.TryGetResultNanoseconds(out long elapsed));
        start=Stopwatch.GetTimestamp();diagnostics.Poll();double poll=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return(elapsed/1e6,submit,poll);
    }

    /// <summary>Loads the production capture program outside warmed measurement intervals.</summary>
    private static LumonSceneCaptureVoxelComputeShader CreateCapture(BinaryShaderApiFixture assets)
    {
        Assert.True(LumonSceneCaptureVoxelComputeShader.TryCreate(assets.Api,out var shader,out string log),log);
        return shader!;
    }
    #endregion
}
