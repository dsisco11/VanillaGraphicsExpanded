using System.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks numerical final composition across source edits, history resets and neighboring rooms.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingPbrLifetimeTests : RenderTestBase
{


    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceLightingPbrLifetimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Retained lighting transitions
    /// <summary>Source removal leaves exactly direct plus emission; restoration recovers indirect with retained consumers.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void SourceChangesReachRetainedComposition(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9, scene, pbrComposition: true);
        var receiver = new RuntimeReceiverSurface(new(.5f, .25f, .125f), Emission: 1f/32, Transmission: 0);
        runtime.Receiver = (_, _) => receiver;
        runtime.EngineUniforms.SunPosition3D = new(0, 0, 1);
        runtime.Cache.Config.LumOn.TemporalAlpha = .9f;
        runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame = 8;
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
        Assert.True(ImageAssertions.Statistics.Inspect(runtime.FinalImage, channelMask: 7).Minimum > .001f);
        using var reference = new Assertions.GpuImageSnapshot(runtime.SceneLinearImage);
        var screen = runtime.Screen.IndirectFullTex;
        var direct = runtime.Direct.Radiance;
        var atlas = runtime.Cache.IrradianceAtlas();
        using var primary = new Assertions.GpuImageSnapshot(runtime.Direct.Radiance!);
        long stableRevision = runtime.Screen.HistoryRevision;
        for (int frame = 0; frame < 8; frame++) runtime.Frame();
        Assert.Equal(stableRevision, runtime.Screen.HistoryRevision);
        foreach (int light in new[] { 0, 32 })
        {
            long revision = runtime.Screen.HistoryRevision;
            runtime.Cache.ChangeBlockLight(light);
            runtime.Frame();
            SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
            SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
            Assert.True(runtime.Screen.HistoryRevision > revision);
            Assert.Same(screen, runtime.Screen.IndirectFullTex);
            Assert.Same(direct, runtime.Direct.Radiance);
            Assert.Same(atlas, runtime.Cache.IrradianceAtlas());
            ImageAssertions.Comparisons.CompareHistory(runtime.Direct.Radiance!, primary).AssertEqual();
            var expectedIncident = SurfaceLightingNumericalRuntimeTests.DiffuseSourceOutgoing(scene);
            SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime, runtime.SceneLinearImage,
                (x, y) => SurfaceLightingPbrRuntimeTests.DiffuseResponse(scene, x, y, receiver, expectedIncident),
                light == 0 ? .002f : .055f, "retained source composition", additiveLayers: primary.Texture);
            if (light != 0) ImageAssertions.Comparisons.CompareHistory(runtime.SceneLinearImage, reference, tolerance: .01f, channelMask: 7).AssertEqual();
        }
    }

    /// <summary>A dark replacement atlas must reject old bright history before source restoration recovers the final image.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void CacheReplacementInvalidatesComposedHistory(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9, scene, pbrComposition: true);
        runtime.Cache.Config.LumOn.TemporalAlpha = .9f;
        runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame = 8;
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene,
            expectedWorldRadiance: SurfaceLightingNumericalRuntimeTests.DiffuseSourceOutgoing(scene));
        Assert.True(ImageAssertions.Statistics.Inspect(runtime.FinalImage, channelMask: 7).Minimum > .001f);
        using var reference = new Assertions.GpuImageSnapshot(runtime.SceneLinearImage);
        var oldAtlas = runtime.Cache.IrradianceAtlas();
        var screen = runtime.Screen.IndirectFullTex;
        long revision = runtime.Screen.HistoryRevision;
        runtime.Cache.ChangeBlockLight(0);
        runtime.Cache.RequestAtlasRecreation();
        runtime.Frame();
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene,
            expectedWorldRadiance: SurfaceLightingNumericalRuntimeTests.DiffuseSourceOutgoing(scene));
        Assert.NotSame(oldAtlas, runtime.Cache.IrradianceAtlas());
        Assert.False(oldAtlas!.IsValid);
        Assert.Same(screen, runtime.Screen.IndirectFullTex);
        Assert.True(runtime.Screen.HistoryRevision > revision);
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime, runtime.SceneLinearImage, (_, _) => Vector3.Zero, .0001f, "dark replacement composition");
        runtime.Cache.ChangeBlockLight(32);
        runtime.Frame();
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene,
            expectedWorldRadiance: SurfaceLightingNumericalRuntimeTests.DiffuseSourceOutgoing(scene));
        ImageAssertions.Comparisons.CompareHistory(runtime.SceneLinearImage, reference, tolerance: .01f, channelMask: 7).AssertEqual();
    }
    #endregion

    #region Spatial rejection
    /// <summary>Bright neighboring rooms cannot illuminate any composed dark-room pixel, including immediately after movement.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void SealedNeighborRejectsLocalizedLeakage(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f), AlternateDarkRooms = true };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9, scene, pbrComposition: true);
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
        Assert.True(ImageAssertions.Statistics.Inspect(runtime.FinalImage, channelMask: 7).Minimum > .001f);
        using var reference = new Assertions.GpuImageSnapshot(runtime.SceneLinearImage);
        foreach (float x in new[] { 8f, 40f, -24f, 0f })
        {
            scene.Position = new(x, 36, 5);
            runtime.Frame();
            if (x != 0) SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime, runtime.SceneLinearImage, (_, _) => Vector3.Zero, .0001f, "dark room after move");
            SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
            SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
            if (x == 0) Assert.True(ImageAssertions.Statistics.Inspect(runtime.FinalImage, channelMask: 7).Minimum > .001f);
            for (int frame = 0; frame < 8; frame++)
            {
                runtime.Frame();
                if (x != 0) SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime, runtime.SceneLinearImage, (_, _) => Vector3.Zero, .0001f, "sealed neighbor composition");
            }
        }
        ImageAssertions.Comparisons.CompareHistory(runtime.SceneLinearImage, reference, tolerance: .01f, channelMask: 7).AssertEqual();
    }
    #endregion

    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion
}
