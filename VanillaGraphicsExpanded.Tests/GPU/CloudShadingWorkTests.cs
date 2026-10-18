using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Records matched trace costs and physical output differences using independently frozen shader assets.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CloudShadingWorkTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Pairs frozen/current executables for one bright-background workload and one held-out night source.</summary>
    [Fact]
    public void WeakDirectionalShadowCost()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_WEAK_LIGHT_MEASUREMENT")=="1","Explicit weak-source measurement required.");
        EnsureContextValid();
        string baselineRoot=Environment.GetEnvironmentVariable("VGE_CLOUD_SHADING_BASELINE_ASSETS")
            ??throw new InvalidOperationException("Frozen baseline assets required.");
        string directory=Environment.GetEnvironmentVariable("VGE_CLOUD_COMPONENT_OUTPUT")
            ??throw new InvalidOperationException("Measurement output directory required.");
        Directory.CreateDirectory(directory);
        using var baselinePrograms=new ComponentShaderPrograms(baselineRoot);
        using var currentPrograms=new ComponentShaderPrograms();
        var settings=CloudVolumeSettings.Default with {ViewRaySampleMax=384,LightRaySampleMax=10,
            ShadowDistance=1500,DetailAmplitude=.25f,EdgeWidth=8,StopTransmittance=0};
        using var baseline=new CloudReconstructionScene(512,256,baselinePrograms,false,81,settings:settings);
        using var current=new CloudReconstructionScene(512,256,currentPrograms,false,81,settings:settings);
        using var baselineRootBounds=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(baselinePrograms.Api));
        using var currentRootBounds=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(currentPrograms.Api));
        using var timer=GpuTimerQuery.Create("Tests.Cloud.WeakSource.Elapsed");
        foreach(string scene in new[]{"day","night"})
        {
            float environment=scene=="day"?1847:.001f,solar=scene=="day"?84431:0,moon=scene=="day"?.00117f:.2f;
            foreach(var view in new[]{baseline,current})
            {
                view.Capture(0,2,environment,1,0,"detail-lit");
                view.SetDirectionalLights(Vector3.Normalize(new(-.243f,.401f,.883f)),
                    Vector3.Normalize(new(-.897f,.346f,-.274f)),3,solar,moon);
            }
            baseline.UseHierarchy(baselineRootBounds);current.UseHierarchy(currentRootBounds);
            for(int warm=0;warm<3;warm++){baseline.RenderTrace(0);current.RenderTrace(0);}
            var before=new List<(double Device,double Submit)>();var after=new List<(double Device,double Submit)>();
            for(int pair=0;pair<5;pair++)
                foreach(bool old in pair%2==0?new[]{true,false}:new[]{false,true})
                    (old?before:after).Add(Measure(old?baseline:current,timer));
            var original=baseline.ReadTraceRecord();var candidate=current.ReadTraceRecord();
            var receipt=new {scene,fullWidth=512,fullHeight=256,traceWidth=256,traceHeight=128,
                settings,environment,solar,moon,warmups=3,pairs=5,
                actualBaselineParameters=baseline.ReadTraceParameters(),actualCurrentParameters=current.ReadTraceParameters(),
                baselineGpuMilliseconds=before.Select(value=>value.Device),currentGpuMilliseconds=after.Select(value=>value.Device),
                pairedSavedGpuMilliseconds=before.Zip(after,(a,b)=>a.Device-b.Device),
                baselineSubmitMilliseconds=before.Select(value=>value.Submit),currentSubmitMilliseconds=after.Select(value=>value.Submit),
                difference=TransportDifferences(original,candidate,256,256,128,1),fixture.GLRenderer,fixture.GLVersion,
                qualification="Contended fixed synthetic dense inside-cloud trace, narrow projection; unchanged settings. Output differences diagnostic; no quality/timing gates or live FPS claims."};
            File.WriteAllText(Path.Combine(directory,scene+".json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions {WriteIndented=true}));
            output.WriteLine("CLOUD_WEAK_SOURCE_MEASUREMENT="+JsonSerializer.Serialize(receipt));
        }
        GlDebug.ThrowIfErrors("Weak-source matched measurements");
    }
    /// <summary>Isolates major trace costs with fixed inputs and paired, rotated opt-in measurements.</summary>
    [Fact]
    public void ComponentCostAblations()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_COMPONENT_MEASUREMENT")=="1","Explicit component measurement required.");
        EnsureContextValid();
        string directory=Environment.GetEnvironmentVariable("VGE_CLOUD_COMPONENT_OUTPUT")
            ??throw new InvalidOperationException("Measurement output directory required.");
        Directory.CreateDirectory(directory);
        using var programs=new ComponentShaderPrograms();
        using var timer=GpuTimerQuery.Create("Tests.Cloud.Component.Elapsed");
        var settings=CloudVolumeSettings.Default with {ViewRaySampleMax=384,LightRaySampleMax=10,
            ShadowDistance=1500,DetailAmplitude=.25f,EdgeWidth=8,StopTransmittance=0};
        (string Name,CloudVolumeSettings Settings,string Scene,int Sources)[] variants=
        [
            ("baseline",settings,"detail-lit",3),
            ("environment-only",settings,"detail-lit",0),
            ("solar-only",settings,"detail-lit",1),
            ("detail-off",settings with {Detail=false},"lit",3),
            ("primary96",settings with {ViewRaySampleMax=96},"detail-lit",3),
            ("shadow2",settings with {LightRaySampleMax=2},"detail-lit",3),
            ("termination005",settings with {StopTransmittance=.005f},"detail-lit",3),
            ("quarter",settings with {TraceScale=CloudTraceScale.Quarter},"detail-lit",3),
            ("baseline-double-extent",settings,"detail-lit",3)
        ];
        var scenes=new List<CloudReconstructionScene>();var roots=new List<CloudHierarchy>();
        var inputs=new List<float[][]>();
        try
        {
            // The immutable field, phase and source directions stay fixed. Only the named
            // factor changes; root construction, shader warmup and readback remain untimed.
            for(int i=0;i<variants.Length;i++)
            {
                var variant=variants[i];int factor=i==8?2:1;
                var scene=new CloudReconstructionScene(512*factor,256*factor,programs,false,81,settings:variant.Settings);
                scenes.Add(scene);scene.Capture(0,2,1847,1,0,variant.Scene);
                scene.SetDirectionalLights(Vector3.Normalize(new(-.243f,.401f,.883f)),
                    Vector3.Normalize(new(-.897f,.346f,-.274f)),variant.Sources,84431,.00117f);
                var root=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(programs.Api));roots.Add(root);
                scene.UseHierarchy(root);
                for(int warm=0;warm<3;warm++)scene.RenderTrace(0);
                var input=scene.ReadTraceParameters();inputs.Add(input);
                Assert.Equal(variant.Settings.ViewRaySampleMax,input[0][12]);
                Assert.Equal(variant.Settings.LightRaySampleMax,input[0][13]);
                Assert.Equal(variant.Settings.Detail?1:0,input[0][11]);
                Assert.Equal(variant.Settings.StopTransmittance,input[0][51]);
                Assert.Equal(variant.Sources%2==1?84431:0,input[0][16]);
                Assert.Equal((variant.Sources&2)!=0?.00117f:0,input[0][20]);
                output.WriteLine($"Component warmup complete: {variant.Name}");
            }
            var baselineTimes=Enumerable.Range(0,variants.Length).Select(_=>new List<(double Device,double Submit)>()).ToArray();
            var candidateTimes=Enumerable.Range(0,variants.Length).Select(_=>new List<(double Device,double Submit)>()).ToArray();
            for(int round=0;round<5;round++)
            {
                for(int offset=0;offset<variants.Length-1;offset++)
                {
                    int index=1+(offset+round)%(variants.Length-1);
                    foreach(bool baseline in round%2==0?new[]{true,false}:new[]{false,true})
                    {
                        var sample=Measure(scenes[baseline?0:index],timer);
                        (baseline?baselineTimes[index]:candidateTimes[index]).Add(sample);
                    }
                }
                output.WriteLine($"Component paired round complete: {round+1}/5");
            }
            float[][] reference=scenes[0].ReadTraceRecord();
            for(int index=1;index<variants.Length;index++)
            {
                var variant=variants[index];var layout=CloudResolutionLayout.Create(index==8?1024:512,index==8?512:256,variant.Settings.TraceScale);
                float[][] current=scenes[index].ReadTraceRecord();
                int occupied=Enumerable.Range(0,current[0].Length/4).Count(pixel=>current[0][pixel*4+3]<1);
                Assert.True(occupied>0,"The measured field must exercise nonempty cloud transport.");
                object? difference=index==8?null:TransportDifferences(reference,current,256,layout.TraceWidth,
                    layout.TraceHeight,variant.Settings.TraceScale==CloudTraceScale.Quarter?2:1);
                var receipt=new {variant=variant.Name,fullWidth=index==8?1024:512,fullHeight=index==8?512:256,
                    traceWidth=layout.TraceWidth,traceHeight=layout.TraceHeight,occupied,
                    settings=variant.Settings,sourceMask=variant.Sources,warmups=3,pairs=5,
                    field=new {nativeWidth=81,nativeExtinction=2f,lowerHeight=-25f,upperHeight=25f,
                        projectionX=4f,projectionY=4f,farClip=1000f,environment=1847f},
                    actualBaselineVolumeParameters=inputs[0][0],actualBaselineTemporalParameters=inputs[0][1],
                    actualCandidateVolumeParameters=inputs[index][0],actualCandidateTemporalParameters=inputs[index][1],
                    baselineGpuMilliseconds=baselineTimes[index].Select(value=>value.Device),
                    candidateGpuMilliseconds=candidateTimes[index].Select(value=>value.Device),
                    pairedGpuMillisecondsSaved=baselineTimes[index].Zip(candidateTimes[index],(a,b)=>a.Device-b.Device),
                    baselineSubmitMilliseconds=baselineTimes[index].Select(value=>value.Submit),
                    candidateSubmitMilliseconds=candidateTimes[index].Select(value=>value.Submit),difference,
                    lumOn=VanillaGraphicsExpanded.PBR.PbrShaderLightingMode.LumOnEnabled,
                    fixture.GLRenderer,fixture.GLVersion,
                    qualification="Contended synthetic dense inside-cloud path with narrow fixed projection, not capture reproduction. Game remains running. Differences are diagnostic; no quality/timing gates or live FPS prediction."};
                string json=JsonSerializer.Serialize(receipt,new JsonSerializerOptions {WriteIndented=true});
                File.WriteAllText(Path.Combine(directory,variant.Name+".json"),json);
                output.WriteLine("CLOUD_COMPONENT_MEASUREMENT="+JsonSerializer.Serialize(receipt));
            }
            GlDebug.ThrowIfErrors("Cloud component measurements");
        }
        finally
        {
            for(int i=scenes.Count-1;i>=0;i--)scenes[i].Dispose();
            for(int i=roots.Count-1;i>=0;i--)roots[i].Dispose();
        }
    }
    /// <summary>Alternates warmed production trace passes without imposing timing or approximation-quality gates.</summary>
    [Fact]
    public void MatchedShadingCostsAndTransportDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_SHADING_MEASUREMENT") != "1")
            Assert.Skip("Set VGE_RUN_CLOUD_SHADING_MEASUREMENT=1 and VGE_CLOUD_SHADING_BASELINE_ASSETS to frozen built assets.");
        EnsureContextValid();
        output.WriteLine("CLOUD_SHADING_DEVICE="+JsonSerializer.Serialize(new {fixture.GLRenderer,fixture.GLVersion}));
        string baselineRoot = Environment.GetEnvironmentVariable("VGE_CLOUD_SHADING_BASELINE_ASSETS")
            ?? throw new InvalidOperationException("A frozen baseline asset root is required.");
        Assert.True(Directory.Exists(baselineRoot), baselineRoot);
        using var baselinePrograms = new ComponentShaderPrograms(baselineRoot);
        using var currentPrograms = new ComponentShaderPrograms();
        var settings = CloudVolumeSettings.Default with { StopTransmittance = 0 };
        using var baseline = new CloudReconstructionScene(32, 32, baselinePrograms, false, 81, settings: settings);
        using var current = new CloudReconstructionScene(32, 32, currentPrograms, false, 81, settings: settings);
        using var timer = GpuTimerQuery.Create("Tests.Cloud.Shading.Elapsed");

        // Identical owners, inputs and schedules isolate the selected built shader assets.
        foreach (string scene in new[] { "homogeneous", "detail-lit", "terrain", "particle-within", "underwater", "aerial", "cloud-edge", "changing-fog" })
        {
            string inputScene=scene=="changing-fog"?"underwater":scene;
            baseline.Capture(0, .2f, 4, 1, 0, inputScene);
            current.Capture(0, .2f, 4, 1, 0, inputScene);
            if(scene=="aerial") {baseline.SetAerialDistanceRamp(20,.8f);current.SetAerialDistanceRamp(20,.8f);}
            if(scene=="changing-fog") {baseline.SetLocalFogDensity(.002f);current.SetLocalFogDensity(.002f);}
            baseline.SetDirectionalLights(Vector3.Normalize(new(1, .05f, 0)), -Vector3.UnitY, 3, 30, 3);
            current.SetDirectionalLights(Vector3.Normalize(new(1, .05f, 0)), -Vector3.UnitY, 3, 30, 3);
            for (int warm = 0; warm < 3; warm++) { baseline.RenderTrace(0); current.RenderTrace(0); }
            var baselineTimes = new List<double>();
            var currentTimes = new List<double>();
            double baselineSubmit = 0, currentSubmit = 0;
            for (int pair = 0; pair < 7; pair++)
            {
                // Reverse the order each pair rather than attributing temporal drift to one candidate.
                foreach (bool old in pair % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    var measured = Measure(old ? baseline : current, timer);
                    (old ? baselineTimes : currentTimes).Add(measured.Device);
                    if (old) baselineSubmit += measured.Submit; else currentSubmit += measured.Submit;
                }
            }
            float[][] before = baseline.ReadTraceRecord(), after = current.ReadTraceRecord();
            int baselineOccupied=Enumerable.Range(0,before[0].Length/4).Count(pixel=>before[0][pixel*4+3]<1);
            int currentOccupied=Enumerable.Range(0,after[0].Length/4).Count(pixel=>after[0][pixel*4+3]<1);
            Assert.True(baselineOccupied>0&&currentOccupied>0,$"{scene}: matched trace fixtures must contain cloud support.");
            var differences = before.Zip(after, (a, b) => a.Zip(b, (x, y) => Math.Abs((double)x - y)).Max()).ToArray();
            double[]? translucentOitDifferences=null;
            if(scene=="particle-within")
            {
                // Resolve the authored translucent surface outside timing; trace alone cannot observe its blend.
                baseline.Render(0,false);current.Render(0,false);
                translucentOitDifferences=baseline.ReadDefault().Zip(current.ReadDefault(),
                    (a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).ToArray();
            }
            double? changedFogMaximumDifference=null;
            if(scene=="changing-fog")
            {
                baseline.SetLocalFogDensity(.02f);current.SetLocalFogDensity(.02f);
                baseline.RenderTrace(0);current.RenderTrace(0);
                before=baseline.ReadTraceRecord();after=current.ReadTraceRecord();
                changedFogMaximumDifference=before.Zip(after,(a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).Max();
            }
            output.WriteLine("CLOUD_SHADING_MEASUREMENT=" + JsonSerializer.Serialize(new
            {
                scene, fullWidth = 32, fullHeight = 32, traceWidth = 16, traceHeight = 16,
                baselineOccupied,currentOccupied,
                warmupPassesPerCandidate = 3, pairedSamples = 7,
                baselineGpuMilliseconds = baselineTimes, currentGpuMilliseconds = currentTimes,
                baselineSubmitMilliseconds = baselineSubmit / 7, currentSubmitMilliseconds = currentSubmit / 7,
                maximumAbsoluteDifferencePerPayloadLayer = differences,
                translucentOitDifferences,
                changedFogMaximumDifference,
                qualification = "Headless synthetic trace only; output differences are diagnostics and appearance remains user reviewed."
            }));
        }
        GlDebug.ThrowIfErrors("Cloud shading measurements");
    }
    #endregion

    #region Private
    /// <summary>Separates physical-channel differences from current spacing/support metadata at matched traced pixels.</summary>
    private static object TransportDifferences(float[][] baseline,float[][] candidate,int baselineWidth,int width,int height,int stride)
    {
        double radiance=0,transmission=0,buckets=0,spacing=0,support=0;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int a=(y*stride*baselineWidth+x*stride)*4,b=(y*width+x)*4;
            for(int channel=0;channel<3;channel++)
            {
                radiance=Math.Max(radiance,Math.Abs((double)baseline[0][a+channel]-candidate[0][b+channel]));
                buckets=Math.Max(buckets,Math.Abs((double)baseline[1][a+channel]-candidate[1][b+channel]));
            }
            transmission=Math.Max(transmission,Math.Abs((double)baseline[0][a+3]-candidate[0][b+3]));
            spacing=Math.Max(spacing,Math.Abs((double)baseline[1][a+3]-candidate[1][b+3]));
            for(int channel=0;channel<4;channel++)support=Math.Max(support,Math.Abs((double)baseline[2][a+channel]-candidate[2][b+channel]));
        }
        return new {maximumRadianceDifference=radiance,maximumTransmissionDifference=transmission,
            maximumBucketDifference=buckets,maximumSpacingDifference=spacing,maximumSupportDifference=support};
    }
    /// <summary>Measures only trace submission and device execution, with readback outside the measured interval.</summary>
    private static (double Device, double Submit) Measure(CloudReconstructionScene scene, GpuTimerQuery timer)
    {
        timer.Begin();
        long started = Stopwatch.GetTimestamp();
        scene.RenderTrace(0);
        double submit = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        timer.End();
        return (timer.GetResultNanoseconds() / 1e6, submit);
    }
    #endregion
}
