using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks world probe trace directions behavior and contract rules.</summary>
public sealed class WorldProbeTraceDirectionsTests
{
    #region Public API
    /// <summary>Verifies the directions count matches octahedral tile behavior.</summary>
    [Fact]
    public void Directions_CountMatchesOctahedralTile()
    {
        // Arrange
        int expectedCount = LumOnWorldProbeTraceDirections.OctahedralSize * LumOnWorldProbeTraceDirections.OctahedralSize;
        // Act
        var dirs = LumOnWorldProbeTraceDirections.GetDirections();
        // Assert
        Assert.Equal(expectedCount, dirs.Length);
    }
    /// <summary>Verifies the directions are normalized behavior.</summary>
    [Fact]
    public void Directions_AreNormalized()
    {
        // Arrange
        const float minimumLengthSquared = 0.999f;
        const float maximumLengthSquared = 1.001f;
        // Act
        var dirs = LumOnWorldProbeTraceDirections.GetDirections();
        // Assert
        foreach (var d in dirs)
        {
            float len2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
            Assert.InRange(len2, minimumLengthSquared, maximumLengthSquared);
        }
    }
    #endregion
}
