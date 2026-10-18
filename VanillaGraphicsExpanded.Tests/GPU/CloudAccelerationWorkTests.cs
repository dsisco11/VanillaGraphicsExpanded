using System.Diagnostics;
using System.Text.Json;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures independently built support policies with the actual reconstruction scheduler.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudAccelerationWorkTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Records matched construction, trace, reconstruction and total costs without performance gates.</summary>
    [Fact]
    public void MatchedSupportPolicies()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_ACCELERATION_MEASUREMENT")=="1","Explicit support-policy qualification required.");
        EnsureContextValid();
        int width=Setting("VGE_CLOUD_MEASURE_WIDTH",64),height=Setting("VGE_CLOUD_MEASURE_HEIGHT",64);
        int rounds=Setting("VGE_CLOUD_MEASURE_ROUNDS",7);
        string directory=Environment.GetEnvironmentVariable("VGE_CLOUD_MEASURE_OUTPUT")??throw new InvalidOperationException("Measurement output required.");
        string roots=Environment.GetEnvironmentVariable("VGE_CLOUD_ACCELERATION_ASSETS")??throw new InvalidOperationException("Frozen policy assets required.");
        Directory.CreateDirectory(directory);
        string[] policies=["direct","root","tree"];
        using var directPrograms=new ComponentShaderPrograms(Path.Combine(roots,policies[0],"assets"));
        using var rootPrograms=new ComponentShaderPrograms(Path.Combine(roots,policies[1],"assets"));
        using var treePrograms=new ComponentShaderPrograms(Path.Combine(roots,policies[2],"assets"));
        ComponentShaderPrograms[] programs=[directPrograms,rootPrograms,treePrograms];
        bool? previous=PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            foreach(bool lumOn in new[]{false,true})
            {
                PbrShaderLightingMode.GenerationLumOnEnabled=lumOn;
                foreach(string sceneName in (Environment.GetEnvironmentVariable("VGE_CLOUD_MEASURE_CASES")??"empty/inside/200,sparse/ground/200,dense/inside/200,separated/above/200,sparse/edge/81").Split(','))
                {
                    string[] parts=sceneName.Split('/');int nativeWidth=int.Parse(parts[2]);
                    var scenes=programs.Select(p=>new CloudPerformanceScene(p,width,height,nativeWidth,parts[0],parts[1])).ToArray();
                    var owners=scenes.Select(scene=>scene.CreateReconstruction()).ToArray();
                    var summaries=programs.Select(p=>new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(p.Api))).ToArray();
                    try
                    {
                        using var timer=new CloudPerformanceTimer();
                        List<double[]>[] costs=[[],[],[]];
                        int[][]? allocations=null;
                        int[]? summaryAllocations=null;
                        for(int round=-3;round<rounds;round++)
                        {
                            CloudMapInput[] inputs=scenes.Select(scene=>scene.Capture((uint)(round+3))).ToArray();
                            // Rotate submission order to distribute clock/thermal drift among all policies.
                            for(int turn=0;turn<3;turn++)
                            {
                                int policy=(turn+round+3)%3;
                                scenes[policy].Clear();timer.Begin();long start=Stopwatch.GetTimestamp();
                                if(policy!=0) Assert.True(summaries[policy].TryBuild(inputs[policy],()=>true),summaries[policy].Failure);
                                timer.ConstructionComplete();
                                Assert.True(scenes[policy].Draw(owners[policy],inputs[policy],policy==0?null:summaries[policy],()=>true));
                                double submit=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                                double[] elapsed=[..timer.End(),submit];
                                if(round>=0)costs[policy].Add(elapsed);
                            }
                            int[][] currentAllocations=scenes.Select((scene,i)=>scene.AllocationIds(owners[i])).ToArray();
                            int[] currentSummaryAllocations=Enumerable.Range(1,2).Select(i=>
                            {
                                Assert.True(summaries[i].TryGetCurrent(inputs[i],out var summary));return summary!.TextureId;
                            }).ToArray();
                            if(allocations is null){allocations=currentAllocations;summaryAllocations=currentSummaryAllocations;}
                            else
                            {
                                for(int policy=0;policy<3;policy++)Assert.Equal(allocations[policy],currentAllocations[policy]);
                                Assert.Equal(summaryAllocations,currentSummaryAllocations);
                            }
                        }
                        // Readback and explicit same-frame comparisons are outside every measured interval.
                        float[][] expected=scenes[0].ReadCommittedHistory(owners[0]);
                        float[][] expectedOit=scenes[0].ReadOutput();
                        double[][] differences=new double[3][];
                        double[][] oitDifferences=new double[3][];
                        for(int policy=0;policy<3;policy++)
                        {
                            float[][] actual=scenes[policy].ReadCommittedHistory(owners[policy]);
                            differences[policy]=expected.Zip(actual,(a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).ToArray();
                            foreach(float[] layer in actual)foreach(float value in layer)Assert.True(float.IsFinite(value));
                            EqualTransport(expected,actual);
                            float[][] actualOit=scenes[policy].ReadOutput();
                            oitDifferences[policy]=expectedOit.Zip(actualOit,(a,b)=>a.Zip(b,(x,y)=>Math.Abs((double)x-y)).Max()).ToArray();
                            EqualTransport(expectedOit,actualOit);
                        }
                        int occupied=expected[0].Where((_,i)=>i%4==3).Count(value=>value<1);
                        if(parts[0]!="empty")Assert.True(occupied>0,"Nonempty fixture must transport clouds.");
                        int[] gamePids=Process.GetProcessesByName("Vintagestory").Select(p=>p.Id).ToArray();
                        var record=new{width,height,nativeWidth,sceneName,lumOn,rounds,warmups=3,
                            renderer=fixture.GLRenderer,version=fixture.GLVersion,gamePids,
                            settings=CloudVolumeSettings.Default,policies,
                            costColumns=new[]{"constructionGpuMs","traceGpuMs","temporalGpuMs","resolveGpuMs","totalGpuMs","submissionCpuMs"},
                            costs,maximumAbsoluteHistoryDifferencePerLayer=differences,maximumAbsoluteOitDifferencePerLayer=oitDifferences,occupied,
                            allocationIds=allocations,summaryAllocationIds=summaryAllocations,
                            note="Actual scheduler and all reduction/barrier costs included; input upload, clear, first-use compilation, readback and query waits excluded. Same sample lattice. Synthetic measurements do not establish game frame-time or appearance."};
                        string path=Path.Combine(directory,$"{width}x{height}-{parts[0]}-{parts[1]}-{nativeWidth}-{lumOn}.json");
                        File.WriteAllText(path,JsonSerializer.Serialize(record,new JsonSerializerOptions{WriteIndented=true}));
                        output.WriteLine($"{sceneName} lumOn={lumOn}: "+string.Join("; ",policies.Select((policy,i)=>$"{policy} total={Median(costs[i].Select(c=>c[4])):F6}ms trace={Median(costs[i].Select(c=>c[1])):F6}ms")));
                    }
                    finally
                    {
                        foreach(var owner in owners)owner.Dispose();foreach(var summary in summaries)summary.Dispose();foreach(var scene in scenes.Reverse())scene.Dispose();
                    }
                }
            }
        }
        finally {PbrShaderLightingMode.GenerationLumOnEnabled=previous;}
        GlDebug.ThrowIfErrors("Cloud support policy measurements");
    }
    #endregion

    #region Private
    /// <summary>Allows only floating-point evaluation differences between identical finite algorithms.</summary>
    private static void EqualTransport(float[][] expected,float[][] actual)
    {
        for(int layer=0;layer<expected.Length;layer++)for(int index=0;index<expected[layer].Length;index++)
        {
            float value=expected[layer][index],observed=actual[layer][index];
            Assert.True(float.IsFinite(observed));
            double tolerance=Math.Abs((double)value)*8e-6;
            Assert.True(Math.Abs((double)value-observed)<=tolerance,$"Equivalent transport layer {layer} value {index}: {value:R} vs {observed:R}.");
        }
    }
    /// <summary>Accepts positive explicit qualification dimensions without increasing routine test work.</summary>
    private static int Setting(string key,int fallback)=>int.TryParse(Environment.GetEnvironmentVariable(key),out int value)&&value>0?value:fallback;
    /// <summary>Computes a distribution median without summing independently ordered stage medians.</summary>
    private static double Median(IEnumerable<double> values)
    {
        double[] sorted=values.Order().ToArray();return(sorted[(sorted.Length-1)/2]+sorted[sorted.Length/2])/2;
    }
    #endregion
}
