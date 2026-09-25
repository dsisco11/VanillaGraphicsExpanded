using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exhaustion readback, delayed retry admission and geometry wake through the production renderer.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceTraversalRuntimeTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Runtime scheduling
    /// <summary>Stable exhausted geometry yields indirect admission to direct refresh and a geometry revision wakes it.</summary>
    [Fact]
    public void ExhaustionDelaysIndirectRetriesAndGeometryChangeWakesThem()
    {
        EnsureContextValid();using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.Config.LumOn.LumonScene.RelightMaxDdaSteps=0;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        runtime.RunUntil(()=>Counter(renderer,"exhaustedBuckets")>0);
        long indirect=Counter(renderer,"indirectFail",attempts:true),direct=Counter(renderer,"refreshFail",attempts:true);
        for(int frame=0;frame<8;frame++)runtime.Frame();
        Assert.Equal(indirect,Counter(renderer,"indirectFail",attempts:true));
        Assert.True(Counter(renderer,"refreshFail",attempts:true)>direct);
        Assert.True(runtime.TryGetLighting(out _));
        runtime.InvalidateGeometry();
        runtime.RunUntil(()=>Counter(renderer,"indirectFail",attempts:true)>indirect,16);
        Assert.True(runtime.TryGetLighting(out _));
    }
    #endregion

    #region Observations
    /// <summary>Reads public diagnostic counters instead of private scheduler state.</summary>
    private static long Counter(LumonSceneRelightUpdateRenderer renderer,string name,bool attempts=false)
    {
        Assert.True(renderer.TryGetSelfCheckLine(out string line));
        var match=Regex.Match(line,$@"\b{name}:(\d+)(?:/(\d+))?");Assert.True(match.Success,line);
        return long.Parse(match.Groups[attempts?2:1].Value);
    }
    #endregion
}
