using System.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Constrains each runtime pixel against analytic constant-radiance enclosure lighting.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class SurfaceLightingNumericalRuntimeTests : RenderTestBase
{


    /// <summary>Uses the exclusive material/graphics context.</summary>
    public SurfaceLightingNumericalRuntimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Analytic gather
    /// <summary>A direct-only uniform enclosure transports decoded block lux through its quantized diffuse material budget.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ConstantRadianceMatchesBothGatherConventions(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new Vector3(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, sh9, scene);
        SeedAndFreeze(runtime);
        var expected = DiffuseSourceOutgoing(scene);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime,scene,expectedWorldRadiance:expected);
        AssertConstantScreenInput(runtime,expected);
        AssertPixels(runtime, runtime.FinalImage, (_, _) => expected, .08f, "analytic enclosure");
    }

    /// <summary>Derives room outgoing radiance from authored raw light, active source calibration and byte material encoding.</summary>
    internal static Vector3 DiffuseSourceOutgoing(SpatialLightingScene scene)
    {
        var calibration=VgeFrameRenderer.CurrentLightingCalibration;
        float raw=Math.Clamp(MathF.Round(Math.Clamp(scene.BlockLight/32f,0,1)*255)/255,0,1);
        double floor=calibration.AccessorBlockReferenceLux>0?calibration.AccessorBlockNeutralFloor:calibration.BlockNeutralFloor;
        double coefficient=calibration.AccessorBlockReferenceLux>0?calibration.AccessorBlockReferenceLux:calibration.BlockReferenceLux;
        double amplitude=Math.Max(raw-floor,0);
        double lux=coefficient*Math.Pow(amplitude,4)/(Math.Pow(amplitude,3)+.064);
        // Integrate the independent diffuse budget before the compact material's unsigned-byte conversion.
        var diffuse=GgxHemisphereReference.CapturedDiffuse(scene.SourceAlbedo??new(scene.Reflectance));
        return diffuse*(float)(lux/Math.PI);
    }

    /// <summary>Checks persistent directional inputs before comparing a constant-field gather to its analytic source.</summary>
    internal static void AssertConstantScreenInput(SurfaceLightingConsumerRuntimeFixture runtime,Vector3 expected)
    {
        var atlas=runtime.Screen.ScreenProbeAtlasFilteredTex!;
        var meta = runtime.Cache.ImageAssertions.Statistics.Inspect(runtime.Screen.ScreenProbeAtlasMetaFilteredTex!, channelMask: 1);
        Assert.True(meta.NonFinite == 0 && meta.Minimum > 0, $"Missing screen direction: {meta}");
        float[] encoded = Enumerable.Range(0, atlas.Width * atlas.Height)
            .SelectMany(_ => new[] { expected.X / 16, expected.Y / 16, expected.Z / 16, 0f }).ToArray();
        runtime.Cache.ImageAssertions.Comparisons.Compare(atlas, encoded, .01f / 16, channelMask: 7).AssertEqual();
        runtime.Cache.ImageAssertions.Statistics.Inspect(atlas, 0, 65504, channelMask: 7).AssertFiniteRange();
    }
    #endregion

    #region Deterministic production scheduling
    /// <summary>Seeds all captured pages together and freezes further bounce generations using normal producer budgets.</summary>
    internal static void SeedAndFreeze(SurfaceLightingConsumerRuntimeFixture runtime)
    {
        var config = runtime.Cache.Config.LumOn.LumonScene;
        config.RelightSeedPagesPerFrame = config.RelightDirectPagesPerFrame = config.RelightIndirectPagesPerFrame = 0;
        config.RelightTexelsPerPagePerFrame = 4096;
        runtime.RunUntil(runtime.Cache.AllRequestedCaptured);
        config.RelightSeedPagesPerFrame = config.RelightDirectPagesPerFrame = config.RelightIndirectPagesPerFrame = 256;
        runtime.Frame();
        Assert.True(runtime.Cache.AllRequestedLightingReady());
        config.RelightSeedPagesPerFrame = config.RelightDirectPagesPerFrame = config.RelightIndirectPagesPerFrame = 0;
    }

    /// <summary>Checks all channels in explicitly indexed image regions, with an absolute bound and finite-value requirement.</summary>
    internal static void AssertPixels(SurfaceLightingConsumerRuntimeFixture runtime, GpuTexture actual,
        Func<int, int, Vector3> expected, float tolerance, string context,
        GpuTexture? additiveLayers = null, VanillaGraphicsExpanded.Rendering.RHI.RHITextureRegion? selection = null)
    {
        Assert.Equal(4, actual.Width); Assert.Equal(4, actual.Height);
        int channels = VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.GetTextureEncoding(actual,
            VanillaGraphicsExpanded.Rendering.RHI.PixelComponentType.Float).Channels ==
            VanillaGraphicsExpanded.Rendering.RHI.PixelChannelLayout.Rgb ? 3 : 4;
        var authored = new float[4 * 4 * channels];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
        {
            var reference = expected(x, y);
            for (int c = 0; c < 3; c++) authored[(y * 4 + x) * channels + c] = reference[c];
        }
        var result = runtime.Cache.ImageAssertions.Comparisons.Compare(actual, authored, tolerance, selection,
            channelMask: 7, additiveLayers: additiveLayers);
        var finite = runtime.Cache.ImageAssertions.Statistics.Inspect(actual, selection: selection, channelMask: 7);
        Assert.True(result.Mismatches == 0 && finite.NonFinite == 0, $"{context}: {result}; {finite}");
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
