using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;

using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks exhaustion readback, delayed retry admission and geometry wake through the production renderer.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class SurfaceTraversalRuntimeTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Public API
    #region Runtime scheduling
    /// <summary>Stable exhausted geometry yields indirect admission to direct refresh and a geometry revision wakes it.</summary>
    [Fact]
    public void ExhaustionDelaysIndirectRetriesAndGeometryChangeWakesThem()
    {
        // Arrange: Publish an exposed wall and force indirect rays to exhaust at zero traversal steps.
        EnsureContextValid();using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true);
        runtime.Config.LumOn.LumonScene.RelightMaxDdaSteps=0;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        var renderer=(LumonSceneRelightUpdateRenderer)runtime.LightingProvider;
        runtime.RunUntil(()=>Counter(renderer,"exhaustedBuckets")>0);
        long indirect=Counter(renderer,"indirectFail",attempts:true),direct=Counter(renderer,"refreshFail",attempts:true);
        // Act: Let stable exhausted geometry run for eight frames, then publish a geometry revision.
        for(int frame=0;frame<8;frame++)runtime.Frame();
        // Assert: Indirect retries pause while direct refresh continues; changed geometry wakes them again.
        Assert.Equal(indirect,Counter(renderer,"indirectFail",attempts:true));
        Assert.True(Counter(renderer,"refreshFail",attempts:true)>direct);
        Assert.True(runtime.TryGetLighting(out _));
        // Act: Publish a geometry revision and wait for it to wake indirect retries.
        runtime.InvalidateGeometry();
        runtime.RunUntil(()=>Counter(renderer,"indirectFail",attempts:true)>indirect,16);
        // Assert: The awakened work still leaves a published lighting result.
        Assert.True(runtime.TryGetLighting(out _));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Reads public diagnostic counters instead of private scheduler state.</summary>
    private static long Counter(LumonSceneRelightUpdateRenderer renderer,string name,bool attempts=false)
    {
        Assert.True(renderer.TryGetSelfCheckLine(out string line));
        var match=Regex.Match(line,$@"\b{name}:(\d+)(?:/(\d+))?");Assert.True(match.Success,line);
        return long.Parse(match.Groups[attempts?2:1].Value);
    }
    #endregion
}
