using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Observes clip-space identity before subdivision rasterization introduces interpolation rounding.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
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
        // Preserve every component of the original 224 by 224 interior observation.
        ImageAssertions.Comparisons.CompareConstant(scope.Output,0,
            selection:new(new(0),16,16,0,224,224,1)).AssertEqual();
    }

    /// <summary>Non-neutral geometry proves the observation captures depth and decor-bias displacement.</summary>
    [Fact]
    public void DisplacedHeightPublishesNonzeroClipAndBiasDelta()
    {
        EnsureContextValid();
        using var scope = new TerrainDetailWorkload(depthBias: true, observeClipDelta: true);
        scope.Select("adaptiveNear");
        scope.Draw();
        float[] deltas = GpuLocalRead.Tuple(scope.Output,128,128);
        int index = 0;
        Assert.InRange(deltas[index + 2], -.041f, -.039f);
        Assert.True(deltas[index + 3] > 0f, "The engine decor bias must respond to the changed depth.");
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
