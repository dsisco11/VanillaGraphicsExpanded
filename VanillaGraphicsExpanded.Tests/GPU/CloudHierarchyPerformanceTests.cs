using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Runs explicitly requested paired cloud-owner measurements on synthetic native publications.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
public sealed class CloudHierarchyPerformanceTests(HeadlessGLFixture fixture, ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Records original DDA and accelerated production costs including each current-generation reduction.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PairedProductionCosts(bool lumOn)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_HIERARCHY_MEASUREMENT")=="1", "Explicit full-size cloud performance qualification required.");
        EnsureShaderTestAvailable();
        Assert.DoesNotContain("llvmpipe", GL.GetString(StringName.Renderer) ?? "", StringComparison.OrdinalIgnoreCase);
        bool? previous=PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled=lumOn;
            int width=Setting("VGE_CLOUD_MEASURE_WIDTH",1846), height=Setting("VGE_CLOUD_MEASURE_HEIGHT",980);
            int rounds=Setting("VGE_CLOUD_MEASURE_ROUNDS",24);
            using var timer=new CloudPerformanceTimer();
            var cases=(from pattern in new[]{"empty","sparse","dense","separated"}
                from viewpoint in new[]{"ground","inside","above"} select (pattern,viewpoint,nativeWidth:200))
                .Concat(new[]{("sparse","edge",81),("separated","edge",81)});
            string? selected=Environment.GetEnvironmentVariable("VGE_CLOUD_MEASURE_CASE");
            var chosen=cases.Where(item=>string.IsNullOrEmpty(selected)||$"{item.Item1}/{item.Item2}/{item.Item3}"==selected).ToArray();
            Assert.NotEmpty(chosen);
            foreach(var item in chosen) Run(width,height,item.Item3,item.Item1,item.Item2,lumOn,rounds,timer);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled=previous; }
    }
    #endregion

    #region Private
    #region Measurement execution
    /// <summary>Warms both owners, alternates paired ordering, checks output and persistent allocations outside timing.</summary>
    private void Run(int width,int height,int nativeWidth,string pattern,string viewpoint,bool lumOn,int rounds,CloudPerformanceTimer timer)
    {
        using var scene=new CloudPerformanceScene(Programs,width,height,nativeWidth,pattern,viewpoint);
        using var original=scene.CreateReconstruction(); using var accelerated=scene.CreateReconstruction();
        using var hierarchy=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(Programs.Api));
        var originalCosts=new List<double[]>(); var acceleratedCosts=new List<double[]>();
        var historyEnabled=new List<bool>(); var selectedCloudPixels=new List<int>();
        int[]? originalIds=null, acceleratedIds=null; int hierarchyId=0;
        for(int round=-4;round<rounds;round++)
        {
            uint frame=(uint)(round+4);
            var input=scene.Capture(frame);
            double[] before=[],after=[];
            // Both paths issue exactly nine timestamps; only the candidate submits the owned reduction.
            if((round&1)==0) { before=Measure(false); after=Measure(true); }
            else { after=Measure(true); before=Measure(false); }
            if(round>=0) { originalCosts.Add(before); acceleratedCosts.Add(after); historyEnabled.Add(scene.HistoryEnabled); }
            Assert.True(hierarchy.TryGetCurrent(input,out var bounds));
            if(originalIds is null) { originalIds=scene.AllocationIds(original); acceleratedIds=scene.AllocationIds(accelerated); hierarchyId=bounds!.TextureId; }
            else { Assert.Equal(originalIds,scene.AllocationIds(original)); Assert.Equal(acceleratedIds,scene.AllocationIds(accelerated)); Assert.Equal(hierarchyId,bounds!.TextureId); }
            if(round==-1 || round==rounds-1)
            {
                var originalHistory=scene.ReadCommittedHistory(original); var acceleratedHistory=scene.ReadCommittedHistory(accelerated);
                for(int layer=0;layer<3;layer++) Assert.Equal(originalHistory[layer],acceleratedHistory[layer]);
                // Repeat matching frames only after both histories have committed the same inputs.
                original.Invalidate(); accelerated.Invalidate();
                scene.Clear(); Assert.True(scene.Draw(original,input,null,()=>true)); var expected=scene.ReadOutput();
                scene.Clear(); Assert.True(scene.Draw(accelerated,input,hierarchy,()=>true)); var actual=scene.ReadOutput();
                for(int layer=0;layer<6;layer++) Assert.Equal(expected[layer],actual[layer]);
                int cloudPixels=actual[1].Count(value=>value<.99999f); selectedCloudPixels.Add(cloudPixels);
                if(pattern!="empty") Assert.True(cloudPixels>0,"Nonempty scene must exercise actual cloud transport.");
            }

            /// <summary>Measures CPU submission separately, excluding query wait, authoring and neutral target clear.</summary>
            double[] Measure(bool enabled)
            {
                scene.Clear(); timer.Begin(); long start=Stopwatch.GetTimestamp();
                if(enabled) Assert.True(hierarchy.TryBuild(input,()=>true),hierarchy.Failure);
                timer.ConstructionComplete();
                Assert.True(scene.Draw(enabled?accelerated:original,input,enabled?hierarchy:null,()=>true));
                double cpu=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                return [..timer.End(),cpu];
            }
        }
        int[] gamePids=Process.GetProcessesByName("Vintagestory").Select(process=>process.Id).ToArray();
        Assert.Contains(true,historyEnabled);
        string directory=Environment.GetEnvironmentVariable("VGE_CLOUD_MEASURE_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory,"cloud-hierarchy-measurements");
        Directory.CreateDirectory(directory);
        string identity=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(CloudHierarchy).Assembly.Location)));
        File.WriteAllText(Path.Combine(directory,$"{width}x{height}-{nativeWidth}-{pattern}-{viewpoint}-{lumOn}.json"),JsonSerializer.Serialize(new {
            width,height,nativeWidth,pattern,viewpoint,lumOn,rounds,warmups=4,verticalFovDegrees=75,lightPublicationCadence=8,
            capturedAtUtc=DateTime.UtcNow,renderer=GL.GetString(StringName.Renderer),glVersion=GL.GetString(StringName.Version),gamePids,contended=gamePids.Length>0,
            productionDllSha256=identity,settings=CloudVolumeSettings.Default,
            costColumns=new[]{"constructionGpuMs","traceGpuMs","temporalGpuMs","resolveGpuMs","totalGpuMs","submissionCpuMs"},
            original=originalCosts,accelerated=acceleratedCosts,
            historyEnabled,selectedCloudPixels,
            pairedSavingsGpuMs=originalCosts.Zip(acceleratedCosts,(a,b)=>a[4]-b[4]).ToArray(),
            originalAllocationIds=originalIds,acceleratedAllocationIds=acceleratedIds,hierarchyId,
            hierarchyPayloadBytes=hierarchy.Layout!.PayloadBytes,
            reconstructionPayloadBytes=144L*((width+1)/2)*((height+1)/2),
            note="Same candidate with hierarchy unavailable versus certified. Total encloses reduction and actual production Draw including preparation/submission. Identical nine timestamp observations per path. Native producer, shared scene HZB, synthetic input upload, neutral target clear, allocation/first-use compilation, readback and query waits excluded. Synthetic controlled GPU costs are not live whole-frame timings."
        },new JsonSerializerOptions{WriteIndented=true}));
        log.WriteLine($"{width}x{height} map={nativeWidth} {pattern}/{viewpoint} LumOn={lumOn}: DDA {Median(originalCosts.Select(a=>a[4])):F4}ms hierarchy {Median(acceleratedCosts.Select(a=>a[4])):F4}ms paired saving {Median(originalCosts.Zip(acceleratedCosts,(a,b)=>a[4]-b[4])):F4}ms");
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Input and summary utilities
    /// <summary>Reads explicit positive qualification controls without changing routine GPU-test cost.</summary>
    private static int Setting(string name,int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name),out int value)&&value>0?value:fallback;
    /// <summary>Returns the central paired distribution statistic without summing stage medians.</summary>
    private static double Median(IEnumerable<double> source)
    {
        double[] sorted=source.Order().ToArray(); return (sorted[(sorted.Length-1)/2]+sorted[sorted.Length/2])/2;
    }
    #endregion
    #endregion
}
