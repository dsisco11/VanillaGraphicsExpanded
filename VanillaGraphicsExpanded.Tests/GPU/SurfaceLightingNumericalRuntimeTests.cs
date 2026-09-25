using System.Numerics;
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
    /// <summary>A direct-only uniform enclosure has outgoing radiance 32 times per-channel reflectance divided by pi.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ConstantRadianceMatchesBothGatherConventions(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new Vector3(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene);
        SeedAndFreeze(runtime);
        for (int i = 0; i < 24; i++) runtime.Frame();
        var expected = scene.SourceAlbedo.Value * (32 / MathF.PI);
        AssertPixels(runtime.FinalPixels(), (_, _) => expected, .08f, "analytic enclosure");
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
