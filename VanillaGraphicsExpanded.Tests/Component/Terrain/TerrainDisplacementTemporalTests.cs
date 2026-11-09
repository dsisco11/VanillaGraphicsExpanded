using VanillaGraphicsExpanded.Tests.Component.Terrain.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;



namespace VanillaGraphicsExpanded.Tests.Component.Terrain;

/// <summary>Checks temporal rejection across changes to displaced geometry.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class TerrainDisplacementTemporalTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Temporal transition regression
    /// <summary>A frame that removes prior displacement must reject the previous displaced history.</summary>
    [Theory]
    [InlineData("adaptiveNeutral")]
    [InlineData("adaptiveFaded")]
    [InlineData("adaptiveMissing")]
    [InlineData("adaptiveZeroAmplitude")]
    public void ReturningToFlatSurfaceRejectsDisplacedHistory(string mode)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scope=new TerrainDetailWorkload();
        // Arrange displaced history first; the following flat-surface draw is the transition under test.
        scope.Select("adaptiveNear");scope.Draw();
        float[] before=GpuLocalRead.Tuple(scope.Output,128,128);
        // Assert the baseline really contains displaced geometry before changing the selected surface.
        Assert.True(before[0]<-.02f);
        Assert.True(before[1]>.9f);
        // Act: render the now-flat surface against the displaced temporal history.
        scope.Select(mode);scope.Draw();
        float[] after=GpuLocalRead.Tuple(scope.Output,128,128);
        // Assert the reactive transition rejects the old displacement and leaves current coverage.
        Assert.InRange(after[0],-.00001f,.00001f);
        Assert.True(after[1]>.9f,"Reactive frame must reject history when a previously displaced surface becomes flat.");
    }
    /// <summary>Unchanged geometry and draws outside the displaced route do not reject temporal history.</summary>
    [Theory]
    [InlineData("adaptiveNear", false, true)]
    [InlineData("adaptiveNear", true, false)]
    [InlineData("triangles", true, true)]
    [InlineData("adaptiveDisabled", true, true)]
    public void StableOrIneligibleGeometryKeepsHistory(string mode, bool reactive, bool eligible)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scope = new TerrainDetailWorkload();
        scope.Select(mode, reactive, eligible);
        // Act: execute the behavior under test and capture its observation.
        scope.Draw();
        float[] result = GpuLocalRead.Tuple(scope.Output,128,128);
        // Assert: compare the observed result with the expected contract.
        Assert.Equal(0f, result[1]);
        if (mode == "adaptiveNear" && eligible)
            Assert.True(result[0] < -.02f, "Stable history must not disable physical displacement.");
        else
            Assert.InRange(result[0], -.00001f, .00001f);
    }
    #endregion
    #endregion

}
