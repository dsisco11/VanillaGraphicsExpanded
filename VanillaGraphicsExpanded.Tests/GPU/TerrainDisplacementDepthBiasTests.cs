using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Observes clip-space identity before subdivision rasterization introduces interpolation rounding.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainDisplacementDepthBiasTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Neutral depth identity
    /// <summary>Neutral or faded geometry must retain the original biased clip interpolation exactly.</summary>
    [Theory]
    [InlineData("adaptiveDisabled")]
    [InlineData("adaptiveNeutral")]
    [InlineData("adaptiveFaded")]
    public void NeutralHeightPreservesBiasedClipAcrossInclinedTriangle(string mode)
    {
        EnsureContextValid();
        using var scope = new TerrainDetailWorkload(depthBias: true, observeClipDelta: true);
        scope.Select(mode);
        scope.Draw();
        float[] deltas = scope.Read();
        // The fixture emits the actual TES clip coordinate minus the original biased
        // barycentric clip coordinate. This isolates stage math from raster precision.
        for (int y = 16; y < 240; y++) for (int x = 16; x < 240; x++)
            for (int component = 0; component < 4; component++)
                Assert.Equal(0f, deltas[(y * 256 + x) * 4 + component]);
    }

    /// <summary>Non-neutral geometry proves the observation captures depth and decor-bias displacement.</summary>
    [Fact]
    public void DisplacedHeightPublishesNonzeroClipAndBiasDelta()
    {
        EnsureContextValid();
        using var scope = new TerrainDetailWorkload(depthBias: true, observeClipDelta: true);
        scope.Select("adaptiveNear");
        scope.Draw();
        float[] deltas = scope.Read();
        int index = (128 * 256 + 128) * 4;
        Assert.InRange(deltas[index + 2], -.041f, -.039f);
        Assert.True(deltas[index + 3] > 0f, "The engine decor bias must respond to the changed depth.");
    }
    #endregion
}
