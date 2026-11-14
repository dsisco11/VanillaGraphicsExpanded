using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Checks independent production setters sharing the probe parameter float vector.</summary>
public sealed class ProbeParameterPackingTests
{
    #region Public API
    /// <summary>Distinct sentinels expose component swaps and neighboring-value clobbering in either write order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemporalAndFilterValuesOccupyIndependentComponents(bool reverse)
    {
        // Arrange
        var parameters = new LumOnProbeParamsUbo();
        // Reverse order exposes setters that overwrite neighboring values in the shared vector.
        Action[] writes = [
            () => parameters.TemporalAlpha = 0.125f,
            () => parameters.HitDistanceRejectThreshold = 2.5f,
            () => parameters.HitDistanceSigma = 7.25f,
            () => parameters.LeakThreshold = 0.75f
        ];
        foreach (var write in reverse ? writes.Reverse() : writes) write();
        float[] expected = [0.125f, 2.5f, 7.25f, 0.75f];
        // Act and Assert: each setting remains independently observable after writes in either order.
        Assert.Equal(expected[0], parameters.TemporalAlpha);
        Assert.Equal(expected[1], parameters.HitDistanceRejectThreshold);
        Assert.Equal(expected[2], parameters.HitDistanceSigma);
        Assert.Equal(expected[3], parameters.LeakThreshold);
    }
    #endregion
}
