using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using System.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies that engine display grading is excluded from persistent lighting and pre-display PBR output.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceLightingDisplayBoundaryTests : RenderTestBase
{
    /// <summary>Uses the exclusive graphics context and restores engine settings after each case.</summary>
    public SurfaceLightingDisplayBoundaryTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Display boundary
    /// <summary>Brightness and gamma changes retain the same HDR image and cache generation; owned final composition grades it later.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DisplayGradingDoesNotEnterSceneLinearLighting(bool sh9)
    {
        EnsureContextValid();
        float brightness = ClientSettings.BrightnessLevel;
        float gamma = ClientSettings.GammaLevel;
        float extraGamma = ClientSettings.ExtraGammaLevel;
        try
        {
            var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
            using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene, pbrComposition: true);
            var receiver = new RuntimeReceiverSurface(new(.75f));
            runtime.Receiver = (_, _) => receiver;
            SurfaceLightingNumericalRuntimeTests.SeedAndFreeze(runtime);
            for (int frame = 0; frame < 24; frame++) runtime.Frame();
            var reference = runtime.SceneLinearPixels();
            var incident = GgxHemisphereReference.CapturedDiffuse(scene.SourceAlbedo.Value) * (32 / MathF.PI);
            SurfaceLightingNumericalRuntimeTests.AssertPixels(reference,
                (x, y) => SurfaceLightingPbrRuntimeTests.DiffuseResponse(scene, x, y, receiver, incident), .08f, "scene-linear HDR");
            Assert.True(reference.Where((_, i) => i % 4 != 3).Max() > 2);
            var displayed = runtime.ComposedPixels();
            for (int i = 0; i < reference.Length; i++)
            {
                if ((i & 3) == 3) continue;
                // Primary remains scene-linear; owned final composition performs the display transfer.
                Assert.InRange(displayed[i], reference[i] - .002f, reference[i] + .002f);
            }
            Assert.Contains("pbr_display_resolve", runtime.LoadedPrograms);
            Assert.True(runtime.Cache.TryGetLighting(out var before));
            long history = runtime.Screen.HistoryRevision;
            foreach (var grading in new[] { (.5f, .8f, 1.2f), (2f, 1.2f, .8f) })
            {
                // These are the real engine settings, not an invented exposure control on the cache.
                ClientSettings.BrightnessLevel = grading.Item1;
                ClientSettings.GammaLevel = grading.Item2;
                ClientSettings.ExtraGammaLevel = grading.Item3;
                for (int frame = 0; frame < 8; frame++) runtime.Frame();
                Assert.Equal(grading.Item1, ClientSettings.BrightnessLevel);
                Assert.Equal(grading.Item2, ClientSettings.GammaLevel);
                Assert.Equal(grading.Item3, ClientSettings.ExtraGammaLevel);
                Assert.True(runtime.Cache.TryGetLighting(out var after));
                Assert.Equal(before.DependencyRevision, after.DependencyRevision);
                Assert.Equal(before.Generation, after.Generation);
                Assert.Same(before.OutgoingRadiance, after.OutgoingRadiance);
                Assert.Equal(history, runtime.Screen.HistoryRevision);
                Assert.Equal(reference, runtime.SceneLinearPixels());
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
}
