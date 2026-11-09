using System.Numerics;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Checks numerical final composition across source edits, history resets and neighboring rooms.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingPbrLifetimeTests : RenderTestBase
{

    #region Public API
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceLightingPbrLifetimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Retained lighting transitions
    /// <summary>Source removal leaves exactly direct plus emission; restoration recovers indirect with retained consumers.</summary>
    [Theory]
    [InlineData(false)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void SourceChangesReachRetainedComposition(bool sh9)
    {
        // Arrange a retained PBR composition with separate direct, indirect, and emission contributions.
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
        using var indirectReference = new Assertions.GpuImageSnapshot(runtime.FinalImage);
        // Assert the scene-linear composition baseline used for source-restoration comparisons.
        using var reference = new Assertions.GpuImageSnapshot(runtime.SceneLinearImage);
        var screen = runtime.Screen.IndirectFullTex;
        var direct = runtime.Direct.Radiance;
        var atlas = runtime.Cache.IrradianceAtlas();
        using var primary = new Assertions.GpuImageSnapshot(runtime.Direct.Radiance!);
        long stableRevision = runtime.Screen.HistoryRevision;
        // Act through stable frames before starting the source transitions.
        for (int frame = 0; frame < 8; frame++) runtime.Frame();
        Assert.Equal(stableRevision, runtime.Screen.HistoryRevision);
        AssertIndirectBoundaries(runtime, true);
        // Act by removing and restoring the source while keeping consumer storage alive.
        foreach (int light in new[] { 0, 32 })
        {
            long revision = runtime.Screen.HistoryRevision;
            runtime.Cache.ChangeBlockLight(light);
            runtime.Frame();
            SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
            SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
            // Assert history updates without reallocating textures or changing the direct layer.
            Assert.True(runtime.Screen.HistoryRevision > revision);
            Assert.Same(screen, runtime.Screen.IndirectFullTex);
            Assert.Same(direct, runtime.Direct.Radiance);
            Assert.Same(atlas, runtime.Cache.IrradianceAtlas());
            ImageAssertions.Comparisons.CompareHistory(runtime.Direct.Radiance!, primary).AssertEqual();
            AssertIndirectBoundaries(runtime, light != 0);
            var expectedIncident = SurfaceLightingNumericalRuntimeTests.DiffuseSourceOutgoing(scene);
            SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime, runtime.SceneLinearImage,
                (x, y) => SurfaceLightingPbrRuntimeTests.DiffuseResponse(scene, x, y, receiver, expectedIncident),
                light == 0 ? .002f : .055f, "retained source composition", additiveLayers: primary.Texture);
            if (light != 0)
                ImageAssertions.Comparisons.CompareHistory(runtime.FinalImage, indirectReference).AssertEqual();
            if (light != 0) ImageAssertions.Comparisons.CompareHistory(runtime.SceneLinearImage, reference, tolerance: .01f, channelMask: 7).AssertEqual();
        }
    }

    /// <summary>A dark replacement atlas must reject old bright history before source restoration recovers the final image.</summary>
    [Theory]
    [InlineData(true)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void CacheReplacementInvalidatesComposedHistory(bool sh9)
    {
        // Arrange bright composition and retain the original atlas, history target, and image baselines.
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9, scene, pbrComposition: true);
        runtime.Cache.Config.LumOn.TemporalAlpha = .9f;
        runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame = 8;
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene,
            expectedWorldRadiance: SurfaceLightingNumericalRuntimeTests.DiffuseSourceOutgoing(scene));
        Assert.True(ImageAssertions.Statistics.Inspect(runtime.FinalImage, channelMask: 7).Minimum > .001f);
        using var indirectReference = new Assertions.GpuImageSnapshot(runtime.FinalImage);
        using var reference = new Assertions.GpuImageSnapshot(runtime.SceneLinearImage);
        var oldAtlas = runtime.Cache.IrradianceAtlas();
        var screen = runtime.Screen.IndirectFullTex;
        long revision = runtime.Screen.HistoryRevision;
        AssertIndirectBoundaries(runtime, true);
        // Act by removing the source and replacing the atlas.
        runtime.Cache.ChangeBlockLight(0);
        runtime.Cache.RequestAtlasRecreation();
        runtime.Frame();
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene,
            expectedWorldRadiance: SurfaceLightingNumericalRuntimeTests.DiffuseSourceOutgoing(scene));
        // Assert replacement invalidates old composed history before source restoration.
        Assert.NotSame(oldAtlas, runtime.Cache.IrradianceAtlas());
        Assert.False(oldAtlas!.IsValid);
        Assert.Same(screen, runtime.Screen.IndirectFullTex);
        Assert.True(runtime.Screen.HistoryRevision > revision);
        AssertIndirectBoundaries(runtime, false);
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime, runtime.SceneLinearImage, (_, _) => Vector3.Zero, .0001f, "dark replacement composition");
        // Act by restoring the source on the replacement atlas.
        runtime.Cache.ChangeBlockLight(32);
        runtime.Frame();
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene,
            expectedWorldRadiance: SurfaceLightingNumericalRuntimeTests.DiffuseSourceOutgoing(scene));
        // Assert restored lighting converges to the retained composition baseline.
        ImageAssertions.Comparisons.CompareHistory(runtime.FinalImage, indirectReference, tolerance: .01f, channelMask: 7).AssertEqual();
        ImageAssertions.Comparisons.CompareHistory(runtime.SceneLinearImage, reference, tolerance: .01f, channelMask: 7).AssertEqual();
    }
    #endregion

    #region Spatial rejection
    /// <summary>Bright neighboring rooms cannot illuminate any composed dark-room pixel, including immediately after movement.</summary>
    [Theory]
    [InlineData(false)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void SealedNeighborRejectsLocalizedLeakage(bool sh9)
    {
        // Arrange adjacent bright and sealed-dark rooms with a published lighting baseline.
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f), AlternateDarkRooms = true };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9, scene, pbrComposition: true);
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
        Assert.True(ImageAssertions.Statistics.Inspect(runtime.FinalImage, channelMask: 7).Minimum > .001f);
        using var reference = new Assertions.GpuImageSnapshot(runtime.SceneLinearImage);
        // Act by moving across the room boundary and back while the production consumers refresh retained history.
        foreach (float x in new[] { 8f, 40f, -24f, 0f })
        {
            scene.Position = new(x, 36, 5);
            runtime.Frame();
            // Assert dark-room pixels remain dark at every moved pose.
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
    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion

    #region Private
    /// <summary>Preserves runtime history, filter, gather and confidence checks separately from scene-linear PBR composition.</summary>
    private void AssertIndirectBoundaries(SurfaceLightingConsumerRuntimeFixture runtime, bool lit)
    {
        foreach (var pixels in new[]
        {
            runtime.Screen.ScreenProbeAtlasHistoryTex!,
            runtime.Screen.ScreenProbeAtlasFilteredTex!,
            runtime.Screen.IndirectHalfTex!,
            runtime.FinalImage
        })
        {
            float peak = runtime.Energy(pixels);
            if (lit) Assert.True(peak > .001f);
            else Assert.InRange(peak, 0, .0001f);
        }

        // FinalImage is the indirect output; SceneLinearImage still contains direct and emissive terms.
        var final = lit
            ? ImageAssertions.Statistics.Inspect(runtime.FinalImage, channelMask: 7)
            : ImageAssertions.Statistics.Inspect(runtime.FinalImage, -.0001f, .0001f, channelMask: 7);
        if (lit)
            Assert.True(final.NonFinite == 0 && final.Minimum > .001f);
        else
            Assert.True(final.NonFinite == 0 && final.OutsideRange == 0);

        Assert.True(ImageAssertions.Statistics.Inspect(runtime.Screen.ScreenProbeAtlasMetaHistoryTex!, channelMask: 1).Maximum > .5f);
    }

    #endregion
}
