using VanillaGraphicsExpanded.Tests.Component.Terrain.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;



namespace VanillaGraphicsExpanded.Tests.Component.Terrain;

/// <summary>Observes clip-space identity before subdivision rasterization introduces interpolation rounding.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class TerrainDisplacementDepthBiasTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    #region Neutral depth identity
    /// <summary>Neutral or faded geometry must retain the original biased clip interpolation exactly.</summary>
    [Theory]
    [InlineData("adaptiveDisabled")]
    [InlineData("adaptiveNeutral")]
    [InlineData("adaptiveFaded")]
    public void NeutralHeightPreservesBiasedClipAcrossInclinedTriangle(string mode)
    {
        // Arrange: Render each neutral/faded mode with depth bias enabled and clip-delta capture active.

        EnsureContextValid();
        using var scope = new TerrainDetailWorkload(depthBias: true, observeClipDelta: true);
        scope.Select(mode);
        // Act: Draw the selected terrain workload to publish the interior clip-delta image.
        scope.Draw();
        // Preserve every component of the original 224 by 224 interior observation.
        // Assert: Every sampled interior pixel retains zero clip-space displacement.
        ImageAssertions.Comparisons.CompareConstant(scope.Output,0,
            selection:new(new(0),16,16,0,224,224,1)).AssertEqual();
    }

    /// <summary>Non-neutral geometry proves the observation captures depth and decor-bias displacement.</summary>
    [Fact]
    public void DisplacedHeightPublishesNonzeroClipAndBiasDelta()
    {
        // Arrange: Select adaptiveNear with depth bias and clip-delta capture enabled.

        EnsureContextValid();
        using var scope = new TerrainDetailWorkload(depthBias: true, observeClipDelta: true);
        scope.Select("adaptiveNear");
        // Act: Draw the displaced terrain and read the center pixel's clip delta.
        scope.Draw();
        float[] deltas = GpuLocalRead.Tuple(scope.Output,128,128);
        int index = 0;
        // Assert: Height displacement changes clip depth by about -0.04 and produces positive decor bias.
        Assert.InRange(deltas[index + 2], -.041f, -.039f);
        Assert.True(deltas[index + 3] > 0f, "The engine decor bias must respond to the changed depth.");
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
