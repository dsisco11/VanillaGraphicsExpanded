using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Records matched trace costs and physical output differences using independently frozen shader assets.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CloudShadingWorkTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    #region Reusable attenuation diagnostics
    /// <summary>Pairs retained uniform generation with one bounded allocation or footprint policy and validates cache invalidation.</summary>
    [Fact]
    public void ShadowAttenuationPrototypeCosts()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_SHADOW_CACHE_MEASUREMENT")=="1","Explicit cache prototype measurement required.");
        EnsureContextValid();
        string directory=Environment.GetEnvironmentVariable("VGE_CLOUD_COMPONENT_OUTPUT")??throw new InvalidOperationException("Output directory required.");
        Directory.CreateDirectory(directory);
        bool? previousMode=VanillaGraphicsExpanded.PBR.PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
        if(Environment.GetEnvironmentVariable("VGE_CLOUD_MEASUREMENT_LUMON") is string selected)
            VanillaGraphicsExpanded.PBR.PbrShaderLightingMode.GenerationLumOnEnabled=selected=="1";
        using var programs=new ComponentShaderPrograms();
        string? coarseRoot=Environment.GetEnvironmentVariable("VGE_CLOUD_COARSE_BASELINE_ASSETS");
        using var coarsePrograms=coarseRoot is null?null:new ComponentShaderPrograms(coarseRoot);
        int policy=int.TryParse(Environment.GetEnvironmentVariable("VGE_CLOUD_CACHE_POLICY"),out int selectedPolicy)?selectedPolicy:1;
        Assert.InRange(policy,0,2);
        var builder=programs.CreateCompute(api=>CloudShadowAttenuationComputeShader.Create(api,policy));
        var baselineBuilder=(coarsePrograms??programs).CreateCompute(api=>CloudShadowAttenuationComputeShader.Create(api,coarseRoot is null?0:1));
        bool segmentedBaseline=Environment.GetEnvironmentVariable("VGE_CLOUD_BASELINE_LAYOUT")=="segmented";
        var controls=CloudVolumeSettings.Default with {TraceScale=CloudTraceScale.Quarter,ViewRaySampleMax=384,ViewRaySampleMin=1,
            LightRaySampleMax=10,ShadowDistance=1500,DetailAmplitude=.25f,EdgeWidth=8,StopTransmittance=0};
        foreach(string scenario in (Environment.GetEnvironmentVariable("VGE_CLOUD_CACHE_CASES")??"small-day,full-day,full-night").Split(','))
        {
            bool full=scenario.StartsWith("full"),night=scenario.EndsWith("night");
            bool grazing=scenario.Contains("grazing");
            bool layered=scenario.Contains("layered"),thin=scenario.Contains("thin");
            Vector3 sun=layered||thin?Vector3.UnitX:Vector3.Normalize(new(-.243f,grazing?.005f:.401f,.883f));
            float solar=night?scenario.Contains("lunar-only")?0:.05f:84431,lunar=night?.2f:20000;
            int width=full?3440:512,height=full?1377:256;
            using var before=new CloudReconstructionScene(width,height,coarsePrograms??programs,false,200,settings:controls);
            using var after=new CloudReconstructionScene(width,height,programs,false,200,settings:controls);
            using var oldRoot=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create((coarsePrograms??programs).Api));
            using var newRoot=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(programs.Api));
            using var target=DynamicTexture2D.Create(256,256,PixelInternalFormat.Rgba32f);
            using var baselineTarget=DynamicTexture2D.Create(256,segmentedBaseline?768:256,PixelInternalFormat.Rgba32f);
            using var timer=new CloudPerformanceTimer();using var rootEnd=GpuTimestampQuery.Create();
            using var start=GpuTimestampQuery.Create();using var end=GpuTimestampQuery.Create();
            var oldTimes=new List<double[]>();var newTimes=new List<double[]>();
            // Both policies rebuild their cache for each publication; alternating pair order
            // separates generation changes from unchanged transport and lookup work.
            for(int round=-3;round<5;round++)foreach(bool old in (round+3)%2==0?new[]{true,false}:new[]{false,true})
            {
                var scene=old?before:after;
                scene.Capture((uint)(round+3),.125f,night?.001f:1847,1,0,"detail-lit");
                if(grazing||scenario.Contains("sparse"))scene.AuthorHierarchyColumns(false);
                if(layered||thin)
                {
                    // Separate native banks expose depth gaps; a shallow supported layer
                    // diagnoses thin features without imposing image-quality assertions.
                    var columns=new float[200*200*4];
                    for(int z=0;z<200;z++) for(int x=0;x<200;x++)
                    {
                        int index=(z*200+x)*4;
                        columns[index]=!layered||x<7||x>=13?.125f:0;
                        columns[index+2]=thin?-.02f:-1;
                        columns[index+3]=thin?.02f:2;
                    }
                    scene.SetShadowColumns(columns);
                }
                scene.SetDirectionalLights(sun,Vector3.Normalize(new(-.897f,.346f,-.274f)),3,solar,lunar);
                start.Issue();long cpu=Stopwatch.GetTimestamp();timer.Begin();
                if(old)scene.BuildShadowCache(baselineBuilder,oldRoot,baselineTarget,64,night?1:0,rootEnd);
                else scene.BuildShadowCache(builder,newRoot,target,64,night?1:0,rootEnd);
                timer.ConstructionComplete();
                scene.RenderShadowCache(0,false);
                double submit=Stopwatch.GetElapsedTime(cpu).TotalMilliseconds;
                double[] values=timer.End(end);long first=start.GetResultNanoseconds();
                double[] sample=[..values,(rootEnd.GetResultNanoseconds()-first)/1e6,(end.GetResultNanoseconds()-first)/1e6,submit];
                if(round>=0)(old?oldTimes:newTimes).Add(sample);
            }
            var reference=before.ReadTraceRecord();var approximate=after.ReadTraceRecord();
            var baselineParameters=before.ReadShadowCacheParameters();
            var cacheParameters=after.ReadShadowCacheParameters();
            float[] cache=target.ReadPixels();
            int empty=Enumerable.Range(0,target.Width*target.Width).Count(i=>cache[i*4+2]==0);
            int occupied=Enumerable.Range(0,target.Width*target.Width).Count(i=>cache[i*4+2]>0);
            int valid=Enumerable.Range(0,target.Width*target.Width)
                .Count(i=>float.IsFinite(cache[i*4])&&float.IsFinite(cache[i*4+1])&&float.IsFinite(cache[i*4+2])
                    &&cache[i*4]>=0&&cache[i*4+1]>=0&&cache[i*4+2]>=0);
            int invalid=target.Width*target.Width-valid,validOccupied=occupied;
            float[] chordLengths=Enumerable.Range(0,target.Width*target.Width).Where(i=>cache[i*4+1]>0)
                .Select(i=>cache[i*4+2]/cache[i*4+1]).Order().ToArray();
            Assert.True(occupied>0,"The prototype must generate nonempty attenuation.");
            // A changed light invalidates the cached source, while a new native publication must also disable its root.
            after.SetDirectionalLights(Vector3.UnitY,-Vector3.UnitY,3,25,5);
            after.RenderTrace(0);var sourceReference=after.ReadTraceRecord();after.RenderShadowCache(0,false);
            double sourceFallback=sourceReference.Zip(after.ReadTraceRecord(),(a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).Max();
            after.Capture(9,.2f,1847,1,.25f,"detail-lit");after.UseHierarchy(null);after.RenderTrace(0);
            var mapReference=after.ReadTraceRecord();after.RenderShadowCache(0,false);
            double mapFallback=mapReference.Zip(after.ReadTraceRecord(),(a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).Max();
            Assert.Equal(0,sourceFallback);Assert.Equal(0,mapFallback);
            after.SetDirectionalLights(sun,Vector3.Normalize(new(-.897f,.346f,-.274f)),3,solar,lunar);
            after.BuildShadowCache(builder,newRoot,target,64,night?1:0);after.RenderShadowCache(0,false);
            float[][] recovered=after.ReadTraceRecord();
            Assert.All(recovered.SelectMany(layer=>layer),value=>Assert.True(float.IsFinite(value)));
            Assert.Contains(target.ReadPixels().Where((_,i)=>i%4==2),value=>value>0);
            var receipt=new {scenario,width,height,nativeWidth=200,field=layered?"separated native banks":thin?"two-unit shallow layer":grazing?"sparse stepped columns":"uniform native columns",grazing,
                cacheWidth=256,representation="compact hardware filtered",baselineRepresentation=segmentedBaseline?"segmented filtered":"compact uniform",storageBytes=target.Width*target.Height*16,
                baselineStorageBytes=baselineTarget.Width*baselineTarget.Height*16,coarseBaseline=coarseRoot,
                quadrature=64,solar,lunar,controls,warmups=3,pairs=5,baselineParameters,cacheParameters,
                columns=new[]{"constructionGpuMs","traceGpuMs","temporalGpuMs","resolveGpuMs","drawTotalGpuMs","rootGpuMs","wholeGpuMs","submitCpuMs"},
                baseline=oldTimes,current=newTimes,baselinePolicy=coarseRoot is null?0:1,currentPolicy=policy,
                baselineGenerationAndBarrierGpuMs=oldTimes.Select(row=>row[0]-row[5]),
                currentGenerationAndBarrierGpuMs=newTimes.Select(row=>row[0]-row[5]),
                empty,occupied,valid,invalid,validOccupied,chords=new {count=chordLengths.Length,minimumWorld=chordLengths.FirstOrDefault(),
                    medianWorld=chordLengths.Length==0?0:chordLengths[chordLengths.Length/2],maximumWorld=chordLengths.LastOrDefault(),
                    maximumUniformSpacingWorld=chordLengths.LastOrDefault()/64},
                sourceFallback,mapFallback,recoveredAfterRebuild=true,
                maximumAbsoluteDifference=reference.Zip(approximate,(a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).ToArray(),
                lumOn=VanillaGraphicsExpanded.PBR.PbrShaderLightingMode.LumOnEnabled,fixture.GLRenderer,
                gamePids=Process.GetProcessesByName("Vintagestory").Select(p=>p.Id).ToArray(),
                qualification=coarseRoot is null?
                    "Opt-in selected generation policy against retained uniform cached generation. Generation includes dispatch/activation/barrier after root; trace includes lookup plus transport; whole is independently timestamped. Differences diagnostic, no appearance gate or live FPS claim.":
                    "Compact hardware-filtered cache against independently frozen generation/trace assets through typed programs. Unchanged declared controls and synthetic native field; changed approximation. Generation includes dispatch/activation/barrier after root; trace includes lookup plus transport; whole is independently timestamped. Concurrent GPU load is uncontrolled; recorded gamePids identify observed game processes; no appearance or native FPS claim."};
            File.WriteAllText(Path.Combine(directory,scenario+".json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));
            output.WriteLine($"Shadow cache {scenario} complete, occupied={occupied}, empty={empty}.");
        }
        GlDebug.ThrowIfErrors("Shadow attenuation prototype");
        }
        finally {VanillaGraphicsExpanded.PBR.PbrShaderLightingMode.GenerationLumOnEnabled=previousMode;}
    }
    #endregion
    #region Complete production owner diagnostics
    /// <summary>Measures complete production cloud owners at representative extents without turning timing or output diagnostics into gates.</summary>
    [Fact]
    public void FullViewProductionOwnerCosts()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_FULL_VIEW_MEASUREMENT")=="1","Explicit full-view measurement required.");
        EnsureContextValid();
        string baselineRoot=Environment.GetEnvironmentVariable("VGE_CLOUD_SHADING_BASELINE_ASSETS")
            ??throw new InvalidOperationException("Frozen baseline required.");
        string directory=Environment.GetEnvironmentVariable("VGE_CLOUD_COMPONENT_OUTPUT")
            ??throw new InvalidOperationException("Output directory required.");
        Directory.CreateDirectory(directory);
        using var baselinePrograms=new ComponentShaderPrograms(baselineRoot);
        using var currentPrograms=new ComponentShaderPrograms(Environment.GetEnvironmentVariable("VGE_CLOUD_SHADING_CANDIDATE_ASSETS"));
        var controls=CloudVolumeSettings.Default with {ViewBudgetDistance=50,TraceScale=CloudTraceScale.Quarter,ViewRaySampleMin=1,ViewRaySampleMax=384,LightRaySampleMax=10,
            ShadowDistance=1500,DetailAmplitude=.25f,EdgeWidth=8,StopTransmittance=0};
        foreach(string budget in (Environment.GetEnvironmentVariable("VGE_CLOUD_PRIMARY_BUDGETS")??"0").Split(','))
        foreach(string mode in (Environment.GetEnvironmentVariable("VGE_CLOUD_FULL_VIEW_CASES")??"day,night,quarter,termination").Split(','))
        {
            bool budgetExperiment=budget!="0";
            bool diagnostic=budgetExperiment||mode is "quarter" or "termination";
            int rounds=budgetExperiment?5:diagnostic?3:5;
            var candidateControls=mode=="quarter"?controls with {TraceScale=CloudTraceScale.Quarter}:
                mode=="termination"?controls with {StopTransmittance=.005f}:controls;
            if(budgetExperiment)candidateControls=controls with {ViewBudgetDistance=float.Parse(budget,System.Globalization.CultureInfo.InvariantCulture)};
            int width=budgetExperiment?512:3440,height=budgetExperiment?256:1377;
            string view=mode=="ground"||mode=="grazing"?"ground":mode=="above"?"above":"inside";
            string field=mode=="opaque-tail"?"opaque-tail":mode=="above"?"separated":mode=="grazing"?"sparse":"dense";
            var candidatePrograms=diagnostic&&!budgetExperiment?baselinePrograms:currentPrograms;
            using var before=new CloudPerformanceScene(baselinePrograms,width,height,200,field,view);
            using var after=new CloudPerformanceScene(candidatePrograms,width,height,200,field,view);
            var lighting=MeasurementLighting(mode=="night");
            if(mode=="grazing")lighting=lighting with {Sun=Vector3.Normalize(new(-.243f,.005f,.883f))};
            before.SetMeasurementInputs(controls,lighting);after.SetMeasurementInputs(candidateControls,lighting);
            using var oldOwner=before.CreateReconstruction();using var newOwner=after.CreateReconstruction();
            using var oldBounds=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(baselinePrograms.Api));
            using var newBounds=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(candidatePrograms.Api));
            using var timer=new CloudPerformanceTimer();using var wholeStart=GpuTimestampQuery.Create();
            using var wholeEnd=GpuTimestampQuery.Create();using var inputEnd=GpuTimestampQuery.Create();
            var oldTimes=new List<double[]>();var newTimes=new List<double[]>();
            for(int round=-3;round<rounds;round++)
            {
                foreach(bool old in (round+3)%2==0?new[]{true,false}:new[]{false,true})
                {
                    double[] sample=MeasureOwner(old?before:after,old?oldOwner:newOwner,old?oldBounds:newBounds,
                        timer,wholeStart,wholeEnd,inputEnd,(uint)(round+3));
                    if(round>=0)(old?oldTimes:newTimes).Add(sample);
                }
            }
            var history=after.ReadCommittedHistory(newOwner);
            var priorHistory=before.ReadCommittedHistory(oldOwner);
            double[] historyDifference=priorHistory.Zip(history,(a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).ToArray();
            if(!diagnostic&&Environment.GetEnvironmentVariable("VGE_CLOUD_MEASUREMENT_APPROXIMATION")!="1") Assert.All(historyDifference.Skip(1),value=>Assert.Equal(0,value));
            var oldTrace=before.ReadLatestTrace(oldOwner);var newTrace=after.ReadLatestTrace(newOwner);
            double[] traceDifference=oldTrace.Zip(newTrace,
                (a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).ToArray();
            if(!diagnostic&&Environment.GetEnvironmentVariable("VGE_CLOUD_MEASUREMENT_APPROXIMATION")!="1")Assert.All(traceDifference.Skip(1),value=>Assert.Equal(0,value));
            double historyTransmissionDifference=Enumerable.Range(0,history[0].Length/4).Max(pixel=>Math.Abs((double)priorHistory[0][pixel*4+3]-history[0][pixel*4+3]));
            double traceTransmissionDifference=Enumerable.Range(0,newTrace[0].Length/4).Max(pixel=>Math.Abs((double)oldTrace[0][pixel*4+3]-newTrace[0][pixel*4+3]));
            if(!diagnostic&&Environment.GetEnvironmentVariable("VGE_CLOUD_MEASUREMENT_APPROXIMATION")!="1")
            {Assert.Equal(0,historyTransmissionDifference);Assert.Equal(0,traceTransmissionDifference);}
            int occupied=history[0].Where((_,index)=>index%4==3).Count(value=>value<1);
            Assert.True(occupied>0,"Full-view field must execute nonempty cloud transport.");
            var receipt=new {mode,comparison=diagnostic?"separate-baseline-policy-diagnostic":Environment.GetEnvironmentVariable("VGE_CLOUD_COMPONENT_LABEL")??"current-default-shadow-policy",
                fullWidth=width,fullHeight=height,nativeWidth=200,view,field,nativeExtinction=mode=="opaque-tail"?6f:.125f,
                baselineControls=controls,candidateControls,defaults=CloudVolumeSettings.Default,warmups=3,rounds,
                columns=new[]{"constructionGpuMs","traceGpuMs","temporalGpuMs","resolveGpuMs","drawTotalGpuMs",
                    "drawSubmitCpuMs","syntheticInputGpuMs","syntheticInputCpuMs","wholeRendererGpuMs","wholeSubmitCpuMs"},
                baseline=oldTimes,current=newTimes,actualBaselineParameters=before.ReadParameters(),actualCurrentParameters=after.ReadParameters(),
                baselinePrimaryWork=budgetExperiment?before.ReadPrimaryWork(oldBounds,true):null,
                currentPrimaryWork=budgetExperiment?after.ReadPrimaryWork(newBounds,false):null,
                historyAdmitted=after.HistoryEnabled,historyDifference,traceDifference,historyTransmissionDifference,traceTransmissionDifference,occupied,maximumRadiance=history[0].Where((_,i)=>i%4!=3).Max(),
                lumOn=VanillaGraphicsExpanded.PBR.PbrShaderLightingMode.LumOnEnabled,
                gamePids=Process.GetProcessesByName("Vintagestory").Select(process=>process.Id).ToArray(),fixture.GLRenderer,fixture.GLVersion,
                qualification="Representative-resolution synthetic production-owner timing, not native scene replay or live FPS; concurrent game PIDs recorded separately. Native producer absent; synthetic input generation/upload timed separately. Whole interval includes upload, output clear, reduction/barriers and all3passes; query waits/readback excluded. Caps are not observed evaluation counts."};
            File.WriteAllText(Path.Combine(directory,mode+(budgetExperiment?"-budget"+budget:"")+".json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
            output.WriteLine($"Full-view {mode} complete; history={after.HistoryEnabled},occupied={occupied}.");
        }
        GlDebug.ThrowIfErrors("Full-view production-owner measurements");
    }
    #endregion
    #region Trace component diagnostics
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
            var receipt=new {comparison=Environment.GetEnvironmentVariable("VGE_CLOUD_COMPONENT_LABEL")??"weak-directional-shadow",
                scene,fullWidth=512,fullHeight=256,traceWidth=256,traceHeight=128,
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
    #endregion

    #region Private
    /// <summary>Supplies captured-style daytime colors or one meaningful held-out lunar publication.</summary>
    private static VanillaGraphicsExpanded.PBR.Atmosphere.AtmosphereLighting MeasurementLighting(bool night)=>
        new(Vector3.Normalize(new(-.24328835f,.4007645f,.8832885f)),night?Vector3.Zero:new(81792.336f,84431.484f,63476),
            night?new(.001f):new(776.72943f,1847.2366f,3910.907f),Vector3.Zero,Vector3.Zero,System.Collections.Immutable.ImmutableArray<float>.Empty)
        {Moon=Vector3.Normalize(new(-.8974143f,.34592077f,-.2738365f)),Lunar=night?new(.2f):new(.0011668546f,.0011682245f,.0008359684f),
            CalibrationGeneration=1,LookupStorageScale=VanillaGraphicsExpanded.PBR.LightingStorage.PersistentScale};

    /// <summary>Times uploads separately and obtains an independent whole-renderer interval before waiting for any query.</summary>
    private static double[] MeasureOwner(CloudPerformanceScene scene,CloudVolumeReconstruction owner,CloudHierarchy bounds,
        CloudPerformanceTimer timer,GpuTimestampQuery wholeStart,GpuTimestampQuery wholeEnd,GpuTimestampQuery inputEnd,uint frame)
    {
        wholeStart.Issue();long inputStart=Stopwatch.GetTimestamp();var input=scene.Capture(frame);
        double inputCpu=Stopwatch.GetElapsedTime(inputStart).TotalMilliseconds;inputEnd.Issue();scene.Clear();
        timer.Begin();long drawStart=Stopwatch.GetTimestamp();Assert.True(bounds.TryBuild(input,()=>true),bounds.Failure);
        timer.ConstructionComplete();Assert.True(scene.Draw(owner,input,bounds,()=>true));
        double drawCpu=Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;
        double wholeCpu=Stopwatch.GetElapsedTime(inputStart).TotalMilliseconds;
        double[] stages=timer.End(wholeEnd);
        long first=wholeStart.GetResultNanoseconds();
        return [..stages,drawCpu,(inputEnd.GetResultNanoseconds()-first)/1e6,inputCpu,
            (wholeEnd.GetResultNanoseconds()-first)/1e6,wholeCpu];
    }
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
