using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks exact observation arithmetic without native resources or a graphics fixture.</summary>
[Trait("Category", "Unit")]
public sealed class GpuObservationLimitsTests
{
    #region Public API
    /// <summary>Counter limits are checked arithmetically without allocating enormous GPU subjects.</summary>
    [Fact]
    public void ObservationDomainLimitsReserveTheMissingCoordinate()
    {
        Assert.Equal(1073741823, GpuObservationLimits.RequireDomain(1073741823, 1, 1));
        Assert.Equal(16777216, GpuObservationLimits.Groups(1073741823));
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuObservationLimits.RequireDomain(1073741824, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuObservationLimits.RequireDomain(1, 0, 1));
        Assert.Throws<OverflowException>(() => GpuObservationLimits.RequireDomain(int.MaxValue, int.MaxValue, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuObservationLimits.Groups(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuObservationLimits.Groups(int.MaxValue));
    }
    #endregion
}
