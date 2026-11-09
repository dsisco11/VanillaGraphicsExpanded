using System.Numerics;
using Vintagestory.Client.NoObf;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Verifies that engine display grading is excluded from persistent lighting and pre-display PBR output.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingDisplayBoundaryTests : RenderTestBase
{

    #region Public API
    /// <summary>Uses the exclusive graphics context and restores engine settings after each case.</summary>
    public SurfaceLightingDisplayBoundaryTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Display boundary
    /// <summary>Brightness and gamma changes retain the same HDR image and cache generation; owned final composition grades it later.</summary>
    [Theory]
    [InlineData(true)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void DisplayGradingDoesNotEnterSceneLinearLighting(bool sh9)
    {
        // Arrange a real composed HDR scene and save global engine grading settings for restoration.
        EnsureContextValid();
        float brightness = ClientSettings.BrightnessLevel;
        float gamma = ClientSettings.GammaLevel;
        float extraGamma = ClientSettings.ExtraGammaLevel;
        try
        {
            var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
            using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9, scene, pbrComposition: true);
            var receiver = new RuntimeReceiverSurface(new(.75f));
            runtime.Receiver = (_, _) => receiver;
            // Act by seeding the cache and rendering a scene-linear baseline.
            SurfaceLightingNumericalRuntimeTests.SeedAndFreeze(runtime);
            for (int frame = 0; frame < 24; frame++) runtime.Frame();
            using var reference = new Assertions.GpuImageSnapshot(runtime.SceneLinearImage);
            var incident = GgxHemisphereReference.CapturedDiffuse(scene.SourceAlbedo.Value) * (32 / MathF.PI);
            SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime, runtime.SceneLinearImage,
                (x, y) => SurfaceLightingPbrRuntimeTests.DiffuseResponse(scene, x, y, receiver, incident), .08f, "scene-linear HDR");
            Assert.True(ImageAssertions.Statistics.Inspect(reference.Texture, channelMask: 7).Maximum > 2);
            // Primary remains scene-linear; owned final composition performs the display transfer.
            ImageAssertions.Comparisons.CompareHistory(runtime.ComposedImage, reference, tolerance: .002f, channelMask: 7).AssertEqual();
            Assert.Contains("pbr_display_resolve", runtime.LoadedPrograms);
            Assert.True(runtime.Cache.TryGetLighting(out var before));
            long history = runtime.Screen.HistoryRevision;
            // Act by varying actual engine brightness and gamma without changing cache inputs.
            foreach (var grading in new[] { (.5f, .8f, 1.2f), (2f, 1.2f, .8f) })
            {
                // These are the real engine settings, not an invented exposure control on the cache.
                ClientSettings.BrightnessLevel = grading.Item1;
                ClientSettings.GammaLevel = grading.Item2;
                ClientSettings.ExtraGammaLevel = grading.Item3;
                for (int frame = 0; frame < 8; frame++) runtime.Frame();
                // Assert grading stays downstream of the retained HDR image and cache generation.
                Assert.Equal(grading.Item1, ClientSettings.BrightnessLevel);
                Assert.Equal(grading.Item2, ClientSettings.GammaLevel);
                Assert.Equal(grading.Item3, ClientSettings.ExtraGammaLevel);
                Assert.True(runtime.Cache.TryGetLighting(out var after));
                Assert.Equal(before.DependencyRevision, after.DependencyRevision);
                Assert.Equal(before.Generation, after.Generation);
                Assert.Same(before.OutgoingRadiance, after.OutgoingRadiance);
                Assert.Equal(history, runtime.Screen.HistoryRevision);
                ImageAssertions.Comparisons.CompareHistory(runtime.SceneLinearImage, reference).AssertEqual();
            }
        }
        finally
        {
            ClientSettings.BrightnessLevel = brightness;
            ClientSettings.GammaLevel = gamma;
            ClientSettings.ExtraGammaLevel = extraGamma;
        }
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
}
