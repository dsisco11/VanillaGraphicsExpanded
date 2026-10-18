using System.Text.Json;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Reads explicit diagnostic-only shader work records without treating them as timing observations.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
public sealed class CloudWorkMeasurementTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Records actual logical loop events from separately instrumented shaders for the controlled scene matrix.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservedPrimaryAndSecondaryWork(bool lumOn)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_WORK_MEASUREMENT")=="1", "Explicit diagnostic shader qualification required.");
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,"cloud-work-diagnostic.json")), "Refuse uninstrumented shader assets.");
        EnsureShaderTestAvailable();
        bool? previous=PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled=lumOn;
            int width=512,height=512;
            var cases=(from pattern in new[]{"empty","sparse","dense","separated"}
                from viewpoint in new[]{"ground","inside","above"} select (pattern,viewpoint,nativeWidth:200))
                .Concat(new[]{("sparse","edge",81),("separated","edge",81)});
            string directory=Environment.GetEnvironmentVariable("VGE_CLOUD_WORK_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory,"cloud-work");
            Directory.CreateDirectory(directory);
            foreach(var item in cases)
            {
                using var scene=new CloudPerformanceScene(Programs,width,height,item.Item3,item.Item1,item.Item2);
                using var reconstruction=scene.CreateReconstruction();
                using var hierarchy=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(Programs.Api));
                var observations=new List<object>();
                uint nextPublication=0;
                foreach(uint frame in new uint[]{4,27})
                {
                    // Replay all source publications so the held light snapshot matches the timed sequence.
                    CloudMapInput input=default;
                    while(nextPublication<=frame) input=scene.Capture(nextPublication++);
                    Assert.True(hierarchy.TryBuild(input,()=>true),hierarchy.Failure);
                    float[][]? original=null;
                    foreach(bool accelerated in new[]{false,true})
                    {
                        reconstruction.Invalidate(); scene.Clear();
                        Assert.True(scene.Draw(reconstruction,input,accelerated?hierarchy:null,()=>true));
                        var storage=Assert.IsType<CloudVolumeStorage>(AccessTools.Field(typeof(CloudVolumeReconstruction),"storage").GetValue(reconstruction));
                        int cursor=(int)AccessTools.Field(typeof(CloudVolumeReconstruction),"cursor").GetValue(reconstruction)!;
                        // The cursor advances after submission; the previous cursor still contains the raw trace.
                        var data=CloudPayloadReadback.Read(storage.Blocks[(cursor+2)%3]);
                        var channels=Enumerable.Range(0,4).Select(channel=>Enumerable.Range(0,data[0].Length/4)
                            .Select(pixel=>data[0][pixel*4+channel]).ToArray()).ToArray();
                        foreach(var channel in channels) foreach(float value in channel) Assert.True(float.IsFinite(value)&&value>=0&&value==MathF.Floor(value));
                        for(int pixel=0;pixel<channels[0].Length;pixel++)
                        {
                            Assert.Equal(4*channels[3][pixel],channels[0][pixel]);
                            Assert.InRange(channels[1][pixel],0,2*channels[0][pixel]);
                        }
                        if(original is null) original=channels;
                        // These observations characterize each compiled diagnostic path. Record divergences
                        // explicitly; replacing physical outputs can change optimization and is not an oracle
                        // for production equivalence, which the uninstrumented scheduler tests separately.
                        var differences=Enumerable.Range(0,4).Select(channel=>new {
                            channel,
                            pixels=Enumerable.Range(0,channels[channel].Length)
                                .Where(pixel=>original[channel][pixel]!=channels[channel][pixel])
                                .Select(pixel=>new {pixel,x=pixel%256,y=pixel/256,dda=original[channel][pixel],hierarchy=channels[channel][pixel]}).ToArray()
                        }).ToArray();
                        if(item.Item1!="empty") Assert.True(channels[0].Sum(value=>(double)value)>0);
                        observations.Add(new{frame,accelerated,
                            channelNames=new[]{"primaryDensityEvaluations","launchedSecondaryRays","secondaryDensityEvaluations","occupiedPrimaryIntervals"},
                            sums=channels.Select(values=>values.Sum(value=>(double)value)).ToArray(),
                            maxima=channels.Select(values=>values.Max()).ToArray(),
                            differences,
                            perPixel=channels});
                    }
                }
                File.WriteAllText(Path.Combine(directory,$"{width}x{height}-{item.Item3}-{item.Item1}-{item.Item2}-{lumOn}.json"),JsonSerializer.Serialize(new{
                    width,height,traceWidth=256,traceHeight=256,lumOn,pattern=item.Item1,viewpoint=item.Item2,nativeWidth=item.Item3,
                    settings=CloudVolumeSettings.Default,observations,
                    note="Actual diagnostic GPU logical event counts; instrumentation replaces raw trace color with counters and can change compiler optimization. No timings are taken from these shaders. Frames 4 and 27 sample the baseline schedule, not all 24 measurement frames. Every DDA/hierarchy count divergence is retained; its cause is unconfirmed and may involve instrumented optimization, boundary arithmetic or traversal. These counts do not qualify production equivalence."
                }));
            }
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled=previous; }
    }
    #endregion
}
