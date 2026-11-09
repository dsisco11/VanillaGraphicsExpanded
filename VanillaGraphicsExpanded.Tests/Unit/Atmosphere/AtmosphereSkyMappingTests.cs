using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Verifies invertible normalized sky elevation mapping across lookup sizes.</summary>
public sealed class AtmosphereSkyMappingTests
{

    #region Public API
    /// <summary>All quality extents and odd extents preserve poles, ordering and unit diffuse response.</summary>
    [Theory]
    [InlineData(.001f)]
    [InlineData(2f)]
    [InlineData(25f)]
    [InlineData(99f)]
    public void MappingIsInvertibleAndNormalized(float altitude)
    {
        // Arrange: use the requested altitude with representative production and odd lookup extents.
        // Act and Assert: derive its horizon, then sample every extent and verify normalized mapping, quadrature weights, monotonic coordinates, and exact poles.
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        foreach (var size in new[] { (32, 24), (64, 48), (96, 72), (128, 96), (17, 9) })
        {
            float sum = 0, previous = -2;
            for (int y = 0; y < size.Item2; y++)
            {
                float v = (float)y / (size.Item2 - 1);
                float elevation = AtmosphereSkyMapping.Elevation(v, horizon);
                Assert.True(elevation > previous);
                Assert.InRange(MathF.Abs(AtmosphereSkyMapping.Coordinate(elevation, horizon) - v), 0, 1e-5f);
                float weight = AtmosphereSkyMapping.EnvironmentWeight(y, size.Item1, size.Item2, horizon);
                Assert.True(weight >= 0);
                sum += weight * size.Item1;
                previous = elevation;
            }
            Assert.InRange(MathF.Abs(sum - 1), 0, 2e-6f);
        }
        Assert.InRange(MathF.Abs(AtmosphereSkyMapping.Elevation(0, horizon) + MathF.PI / 2), 0, 2e-7f);
        Assert.InRange(MathF.Abs(AtmosphereSkyMapping.Elevation(1, horizon) - MathF.PI / 2), 0, 2e-7f);
        Assert.Equal(horizon, AtmosphereSkyMapping.Elevation(.5f, horizon));
    }
    #endregion
}
