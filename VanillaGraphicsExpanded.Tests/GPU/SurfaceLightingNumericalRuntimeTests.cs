using System.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Constrains each runtime pixel against analytic constant-radiance enclosure lighting.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
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
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene);
        SeedAndFreeze(runtime);
        var expected = DiffuseSourceOutgoing(scene);
        SurfaceLightingRefreshSynchronization.CompleteConsumers(runtime,scene,expectedWorldRadiance:expected);
        AssertConstantScreenInput(runtime,expected);
        AssertPixels(runtime.FinalPixels(), (_, _) => expected, .08f, "analytic enclosure");
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
        var radiance=atlas.ReadPixels();
        var meta=runtime.Screen.ScreenProbeAtlasMetaFilteredTex!.ReadPixels();
        // Both local hits and distant-cache fallbacks must arrive in the same physical units.
        // The fixed cache encoding is decoded only for RGB; confidence keeps its own meaning.
        for(int i=0;i<atlas.Width*atlas.Height;i++)
        {
            Assert.True(meta[i*2]>0,$"Missing screen direction ({i%atlas.Width},{i/atlas.Width}).");
            for(int channel=0;channel<3;channel++)
            {
                float physical=VanillaGraphicsExpanded.PBR.LightingStorage.DecodePersistent(radiance[i*4+channel]);
                Assert.True(Math.Abs(physical-expected[channel])<=.01f,
                    $"Screen direction ({i%atlas.Width},{i/atlas.Width}), channel={channel}: expected physical {expected[channel]}, actual {physical}.");
            }
        }
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
    internal static void AssertPixels(float[] actual, Func<int, int, Vector3> expected, float tolerance, string context)
    {
        Assert.Equal(4 * 4 * 4, actual.Length);
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
        {
            var reference = expected(x, y);
            for (int c = 0; c < 3; c++)
            {
                float value = actual[(y * 4 + x) * 4 + c];
                Assert.True(float.IsFinite(value) && Math.Abs(value - reference[c]) <= tolerance,
                    $"{context} ({x},{y}) channel {c}: expected {reference[c]} +/- {tolerance}, actual {value}");
            }
        }
    }
    #endregion
}
