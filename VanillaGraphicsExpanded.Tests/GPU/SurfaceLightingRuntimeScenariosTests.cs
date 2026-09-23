using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Changes engine scene inputs and observes the mod-owned cache, probe pipeline and full-resolution output.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceLightingRuntimeScenariosTests : RenderTestBase
{
    /// <summary>Uses the exclusive material and graphics context.</summary>
    public SurfaceLightingRuntimeScenariosTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Source scenarios
    /// <summary>Source changes invalidate populated history and reach every output pixel through both gather modes.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RetainedHistoryFollowsSourceLighting(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { Reflectance = .25f };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene);
        runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame = 8;
        runtime.Cache.Config.LumOn.TemporalAlpha = .9f;
        SeedAndPause(runtime);
        Settle(runtime, true);
        float[] reference = runtime.FinalPixels();
        long revision = runtime.Screen.HistoryRevision;
        runtime.Cache.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 0;
        for (int i=0;i<8;i++) runtime.Frame();
        Assert.Equal(revision, runtime.Screen.HistoryRevision);
        AssertBoundaries(runtime, true);
        foreach (int light in new[] { 0, 32 })
        {
            revision = runtime.Screen.HistoryRevision;
            runtime.Cache.ChangeBlockLight(light);
            runtime.Frame();
            SeedAndPause(runtime);
            Settle(runtime, light != 0);
            Assert.True(runtime.Screen.HistoryRevision > revision);
            AssertBoundaries(runtime, light != 0);
            if (light != 0) Assert.Equal(reference, runtime.FinalPixels());
        }
    }

    /// <summary>Emission is visible with zero diffuse reflectance; disabling its source policy removes all lighting.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmissiveOnlyCacheLightsRuntimePixels(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { Reflectance = 0, Emission = 4, BlockLight = 0 };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene);
        runtime.Cache.Config.LumOn.LumonScene.SurfaceLightingMaterialEmission = true;
        Settle(runtime, true);
        AssertBoundaries(runtime, true);
        runtime.Cache.Config.LumOn.LumonScene.SurfaceLightingMaterialEmission = false;
        runtime.Frame();
        Settle(runtime, false);
        AssertBoundaries(runtime, false);
        runtime.Cache.Config.LumOn.LumonScene.SurfaceLightingMaterialEmission = true;
        runtime.Frame();
        Settle(runtime, true);
        AssertBoundaries(runtime, true);
    }

    /// <summary>Successive published bounce generations reach final lighting without invalidating unchanged probe history.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ProgressiveBounceReachesRuntimePixels(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { Reflectance = .5f };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene);
        SeedAndPause(runtime);
        Settle(runtime, true);
        long revision = runtime.Screen.HistoryRevision;
        float[] seed = runtime.FinalPixels();
        AdvanceBounce(runtime);
        Settle(runtime, true);
        float[] first = runtime.FinalPixels();
        Assert.True(first[0] > seed[0] * 1.1f, $"First final-pixel bounce: {seed[0]} -> {first[0]}");
        AdvanceBounce(runtime);
        Settle(runtime, true);
        Assert.True(runtime.FinalPixels()[0] > first[0] * 1.01f);
        Assert.Equal(revision, runtime.Screen.HistoryRevision);
        AssertBoundaries(runtime, true);
    }
    #endregion

    #region Geometry and validity
    /// <summary>A closed door rejects exterior illumination and reopening restores it without replacing the runtime.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DoorwayClosureAndReopeningReachRuntimePixels(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { Position = new(0,36,3), DividedRoom = true, Reflectance = .25f };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene);
        runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame = 8;
        runtime.Cache.Config.LumOn.TemporalAlpha = .9f;
        SeedAndPause(runtime);
        Settle(runtime, true);
        float[] reference = runtime.FinalPixels();
        foreach (bool open in new[] { false, true })
        {
            long revision = runtime.Screen.HistoryRevision;
            scene.DoorOpen = open;
            Assert.Equal(32, scene.Light(0,36,6));
            Assert.Equal(0, scene.Light(0,36,4));
            runtime.Cache.InvalidateGeometry();
            runtime.Frame();
            SeedAndPause(runtime);
            Settle(runtime, open);
            Assert.True(runtime.Screen.HistoryRevision > revision);
            AssertBoundaries(runtime, open);
            if (open) AssertClose(reference, runtime.FinalPixels(), .01f);
        }
    }

    /// <summary>Unloaded source chunks reject old lighting, while resident unlit geometry produces confident darkness.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnavailableGeometryDiffersFromValidDarkness(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { Reflectance = .25f };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene);
        Settle(runtime, true);
        foreach (var page in scene.Feedback())
            scene.Unloaded.TryAdd(VanillaGraphicsExpanded.Voxels.ChunkProcessing.ChunkKey.FromChunkCoords(page.Chunk.X,page.Chunk.Y,page.Chunk.Z),0);
        runtime.Frame();
        runtime.RunUntil(() => IsDark(runtime.FinalPixels()) && IsDark(runtime.WorldPixels()));
        Assert.All(runtime.WorldBuffers.Resources!.ProbeMeta0.ReadPixels().Where((_,i)=>i%2==0), value=>Assert.Equal(0,value));
        scene.BlockLight = 0;
        scene.Unloaded.Clear();
        runtime.Cache.InvalidateGeometry();
        runtime.Frame();
        Settle(runtime, false);
        AssertBoundaries(runtime, false);
        Assert.Contains(runtime.WorldBuffers.Resources!.ProbeMeta0.ReadPixels().Where((_,i)=>i%2==0),value=>value>=.25f);
    }
    /// <summary>A dark replacement cache cannot reuse populated final lighting, and restoring its source restores the seeded image.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RecreatedDarkCacheRejectsRetainedFinalLighting(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { Reflectance = .25f };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene);
        SeedAndPause(runtime);
        Settle(runtime, true);
        float[] reference = runtime.FinalPixels();
        var oldAtlas = runtime.Cache.IrradianceAtlas();
        long revision = runtime.Screen.HistoryRevision;
        runtime.Cache.ChangeBlockLight(0);
        runtime.Cache.RequestAtlasRecreation();
        runtime.Frame();
        SeedAndPause(runtime);
        Settle(runtime, false);
        Assert.NotSame(oldAtlas, runtime.Cache.IrradianceAtlas());
        Assert.False(oldAtlas!.IsValid);
        Assert.True(runtime.Screen.HistoryRevision > revision);
        AssertBoundaries(runtime, false);
        runtime.Cache.ChangeBlockLight(32);
        runtime.Frame();
        SeedAndPause(runtime);
        Settle(runtime, true);
        AssertClose(reference, runtime.FinalPixels(), .01f);
    }

    #endregion

    #region Observations and budgets
    /// <summary>Uses public producer budgets to seed all captured pages together, then pauses progressive bounces for matched image comparisons.</summary>
    private static void SeedAndPause(SurfaceLightingConsumerRuntimeFixture runtime)
    {
        var config = runtime.Cache.Config.LumOn.LumonScene;
        config.RelightMaxPagesPerFrame = 0;
        config.RelightTexelsPerPagePerFrame = 4096;
        runtime.RunUntil(runtime.Cache.AllRequestedCaptured);
        config.RelightMaxPagesPerFrame = 256;
        runtime.Frame();
        Assert.True(runtime.Cache.AllRequestedLightingReady());
        config.RelightMaxPagesPerFrame = 0;
    }

    /// <summary>Admits one complete production bounce generation through the normal registered frame, then freezes it for consumer observation.</summary>
    private static void AdvanceBounce(SurfaceLightingConsumerRuntimeFixture runtime)
    {
        Assert.True(runtime.Cache.TryGetLighting(out var before));
        runtime.Cache.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 256;
        runtime.Frame();
        runtime.Cache.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 0;
        Assert.True(runtime.Cache.TryGetLighting(out var after));
        Assert.Equal(before.DependencyRevision, after.DependencyRevision);
        Assert.Equal(before.Generation + 1, after.Generation);
    }

    /// <summary>Preserves the previous per-pixel restoration tolerance at the real runtime output.</summary>
    private static void AssertClose(float[] expected, float[] actual, float tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i=0;i<actual.Length;i++) if (i%4!=3)
            Assert.InRange(Math.Abs(actual[i]-expected[i]),0,tolerance);
    }

    /// <summary>Requires complete cache readiness and valid world data within the shared 160-frame transition budget.</summary>
    private static void Settle(SurfaceLightingConsumerRuntimeFixture runtime, bool lit)
    {
        runtime.RunUntil(() => runtime.Cache.AllRequestedLightingReady() && runtime.Screen.IndirectFullTex != null
            && runtime.WorldBuffers.Resources != null
            && runtime.WorldBuffers.Resources.ProbeMeta0.ReadPixels().Where((_,i)=>i%2==0).Any(v=>v>=.25f)
            && (lit ? AllLit(runtime.FinalPixels()) : IsDark(runtime.FinalPixels()) && IsDark(runtime.WorldPixels())), SurfaceLightingConsumerRuntimeFixture.FrameBudget - 8);
        // Complete a directional sweep so intermediate assertions cannot pass on a transient final output.
        for (int frame=0;frame<8;frame++) runtime.Frame();
    }

    /// <summary>Checks each produced lighting boundary; all final RGB channels must agree with the scene state.</summary>
    private static void AssertBoundaries(SurfaceLightingConsumerRuntimeFixture runtime, bool lit)
    {
        foreach (var pixels in new[] { runtime.Screen.ScreenProbeAtlasHistoryTex!.ReadPixels(),
            runtime.Screen.ScreenProbeAtlasFilteredTex!.ReadPixels(), runtime.Screen.IndirectHalfTex!.ReadPixels(), runtime.FinalPixels() })
        {
            float peak = SurfaceLightingConsumerRuntimeFixture.Energy(pixels);
            if (lit) Assert.True(peak>.001f); else Assert.InRange(peak,0,.0001f);
        }
        Assert.True(lit ? AllLit(runtime.FinalPixels()) : IsDark(runtime.FinalPixels()));
        Assert.Contains(runtime.Screen.ScreenProbeAtlasMetaHistoryTex!.ReadPixels().Where((_,i)=>i%2==0), value=>value>.5f);
    }

    /// <summary>Requires finite nonzero illumination in every final RGB channel.</summary>
    private static bool AllLit(float[] pixels) => pixels.Length>0 && pixels.Where((_,i)=>i%4!=3).All(v=>float.IsFinite(v)&&v>.001f);

    /// <summary>Requires finite darkness across every RGB channel.</summary>
    private static bool IsDark(float[] pixels) => pixels.Length>0 && pixels.Where((_,i)=>i%4!=3).All(v=>float.IsFinite(v)&&Math.Abs(v)<=.0001f);

    #endregion
}
