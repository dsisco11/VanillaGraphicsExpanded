using System.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks numerical final composition across source edits, history resets and neighboring rooms.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceLightingPbrLifetimeTests : RenderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceLightingPbrLifetimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Retained lighting transitions
    /// <summary>Source removal leaves exactly direct plus emission; restoration recovers indirect with retained consumers.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SourceChangesReachRetainedComposition(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene, pbrComposition: true);
        var receiver = new RuntimeReceiverSurface(new(.5f, .25f, .125f), Emission: .5f, Reflectivity: 1);
        runtime.Receiver = (_, _) => receiver;
        runtime.EngineUniforms.SunPosition3D = new(0, 0, 1);
        runtime.Cache.Config.LumOn.TemporalAlpha = .9f;
        runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame = 8;
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
        Assert.All(runtime.FinalPixels().Where((_, i) => i % 4 != 3), value => Assert.True(value > .001f));
        var reference = runtime.ComposedPixels();
        var screen = runtime.Screen.IndirectFullTex;
        var direct = runtime.Direct.DirectDiffuseTex;
        var atlas = runtime.Cache.IrradianceAtlas();
        var primary = PrimaryLighting(runtime);
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
            Assert.Same(direct, runtime.Direct.DirectDiffuseTex);
            Assert.Same(atlas, runtime.Cache.IrradianceAtlas());
            Assert.Equal(primary, PrimaryLighting(runtime));
            var expectedIncident = scene.SourceAlbedo.Value * (light / MathF.PI);
            SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.ComposedPixels(), (x, y) =>
            {
                int offset = (y * 4 + x) * 4;
                return new Vector3(primary[offset], primary[offset + 1], primary[offset + 2])
                    + SurfaceLightingPbrRuntimeTests.DiffuseResponse(scene, x, y, receiver, expectedIncident);
            }, light == 0 ? .002f : .055f, "retained source composition");
            if (light != 0) AssertClose(reference, runtime.ComposedPixels(), .01f);
        }
    }

    /// <summary>A dark replacement atlas must reject old bright history before source restoration recovers the final image.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CacheReplacementInvalidatesComposedHistory(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene, pbrComposition: true);
        runtime.Cache.Config.LumOn.TemporalAlpha = .9f;
        runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame = 8;
        // The production material atlas stores albedo in RGBA8 UNORM before lighting.
        var albedo = scene.SourceAlbedo ?? new Vector3(scene.Reflectance);
        var capturedAlbedo = new Vector3(MathF.Round(albedo.X * 255), MathF.Round(albedo.Y * 255), MathF.Round(albedo.Z * 255)) / 255;
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene, expectedWorldRadiance: capturedAlbedo * (scene.BlockLight / MathF.PI));
        Assert.All(runtime.FinalPixels().Where((_, i) => i % 4 != 3), value => Assert.True(float.IsFinite(value) && value > .001f));
        var reference = runtime.ComposedPixels();
        var oldAtlas = runtime.Cache.IrradianceAtlas();
        var screen = runtime.Screen.IndirectFullTex;
        long revision = runtime.Screen.HistoryRevision;
        runtime.Cache.ChangeBlockLight(0);
        runtime.Cache.RequestAtlasRecreation();
        runtime.Frame();
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene, expectedWorldRadiance: capturedAlbedo * (scene.BlockLight / MathF.PI));
        Assert.NotSame(oldAtlas, runtime.Cache.IrradianceAtlas());
        Assert.False(oldAtlas!.IsValid);
        Assert.Same(screen, runtime.Screen.IndirectFullTex);
        Assert.True(runtime.Screen.HistoryRevision > revision);
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.ComposedPixels(), (_, _) => Vector3.Zero, .0001f, "dark replacement composition");
        runtime.Cache.ChangeBlockLight(32);
        runtime.Frame();
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene, expectedWorldRadiance: capturedAlbedo * (scene.BlockLight / MathF.PI));
        AssertClose(reference, runtime.ComposedPixels(), .01f);
    }
    #endregion

    #region Spatial rejection
    /// <summary>Bright neighboring rooms cannot illuminate any composed dark-room pixel, including immediately after movement.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SealedNeighborRejectsLocalizedLeakage(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f), AlternateDarkRooms = true };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene, pbrComposition: true);
        SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
        Assert.All(runtime.FinalPixels().Where((_, i) => i % 4 != 3), value => Assert.True(value > .001f));
        var reference = runtime.ComposedPixels();
        foreach (float x in new[] { 8f, 40f, -24f, 0f })
        {
            scene.Position = new(x, 36, 5);
            runtime.Frame();
            if (x != 0) SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.ComposedPixels(), (_, _) => Vector3.Zero, .0001f, "dark room after move");
            SurfaceLightingRefreshSynchronization.RefreshAndFreeze(runtime);
            SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime, scene);
            if (x == 0) Assert.All(runtime.FinalPixels().Where((_, i) => i % 4 != 3), value => Assert.True(value > .001f));
            for (int frame = 0; frame < 8; frame++)
            {
                runtime.Frame();
                if (x != 0) SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.ComposedPixels(), (_, _) => Vector3.Zero, .0001f, "sealed neighbor composition");
            }
        }
        AssertClose(reference, runtime.ComposedPixels(), .01f);
    }
    #endregion

    #region Observations
    /// <summary>Observes the independently rendered primary lighting terms for a matched source-disabled baseline.</summary>
    private static float[] PrimaryLighting(SurfaceLightingConsumerRuntimeFixture runtime)
    {
        var diffuse = runtime.Direct.DirectDiffuseTex!.ReadPixels();
        var specular = runtime.Direct.DirectSpecularTex!.ReadPixels();
        var emission = runtime.Direct.EmissiveTex!.ReadPixels();
        for (int i = 0; i < diffuse.Length; i++) diffuse[i] += specular[i] + emission[i];
        return diffuse;
    }

    /// <summary>Preserves channel-by-channel restoration bounds without reducing the image to a peak.</summary>
    private static void AssertClose(float[] expected, float[] actual, float tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++) if (i % 4 != 3)
            Assert.InRange(Math.Abs(actual[i] - expected[i]), 0, tolerance);
    }
    #endregion
}
