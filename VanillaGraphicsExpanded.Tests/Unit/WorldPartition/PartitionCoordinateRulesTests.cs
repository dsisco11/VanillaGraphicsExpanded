using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.WorldPartition;

/// <summary>Verifies coordinate quantization and half-open interval intersection rules.</summary>
public sealed class PartitionCoordinateRulesTests
{
    #region Public API
    /// <summary>Floor mapping preserves negative fractional coordinates and distinct axis extents.</summary>
    [Theory]
    [InlineData(-0.25, -1)]
    [InlineData(-16, -1)]
    [InlineData(-16.25, -2)]
    [InlineData(0, 0)]
    [InlineData(15.99, 0)]
    [InlineData(16, 1)]
    [InlineData(16777216.25, 1048576)]
    [InlineData(-16777216.25, -1048577)]
    public void FloorMapping(double x, long expected)
    {
        // Arrange
        var layout = new PartitionLayout(new(16, 8, 0.25));
        // Act and Assert
        Assert.Equal(new PartitionCoordinate(expected, -1, 1), layout.Coordinate(new(x, -0.125, 0.375)));
        Assert.Equal(new PartitionBounds(new(0, 0, 0), new(16, 8, 0.25)), layout.Bounds(new(0, 0, 0)));
    }

    /// <summary>Intersection includes slivers but excludes the upper boundary and empty intervals.</summary>
    [Fact]
    public void HalfOpenIntersection()
    {
        // Arrange
        var layout = new PartitionLayout(new(16, 16, 16));
        // Act and Assert
        Assert.Equal(new[] { new PartitionCoordinate(-1, 0, 0) }, layout.Intersecting(new(new(-16, 0, 0), new(0, 1, 1))));
        Assert.Equal(2, layout.Intersecting(new(new(15.99, 0, 0), new(16.01, 1, 1))).Count());
        Assert.Empty(layout.Intersecting(new(new(0, 0, 0), new(0, 1, 1))));
    }

    #endregion
}











