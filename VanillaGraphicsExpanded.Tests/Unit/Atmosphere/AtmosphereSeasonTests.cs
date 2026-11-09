using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks temperature bounds and seasonal lag in the atmosphere season model.</summary>
public sealed class AtmosphereSeasonTests
{
    #region Public API
    /// <summary>Warm regions remain bare, cold regions brighten, and spring snow persists longer than autumn snow.</summary>
    [Fact]
    public void TemperatureBoundsAndSeasonalLag()
    {
        // Arrange: select warm, cold and opposite spring/autumn temperature profiles plus invalid-input cases.
        // Act and Assert: perform the operation and check its observable result.
        Assert.Equal((0f, .1f), AtmosphereSeasonModel.Estimate(30, 28, 32));
        Assert.Equal((1f, .8f), AtmosphereSeasonModel.Estimate(-30, -30, -30));
        Assert.True(AtmosphereSeasonModel.Estimate(0, -4, 4).SnowCoverage > AtmosphereSeasonModel.Estimate(0, 4, -4).SnowCoverage);
        Assert.Equal((0f, .1f), AtmosphereSeasonModel.Estimate(float.NaN, 0, 0));
        Assert.Equal((0f, .1f), AtmosphereSeasonModel.Estimate(0, float.PositiveInfinity, 0));
        Assert.Equal(5, AtmosphereSeasonModel.AlbedoBucket(float.NaN));
        Assert.Equal(5, AtmosphereSeasonModel.AlbedoBucket(.101f));
    }
    #endregion
}
