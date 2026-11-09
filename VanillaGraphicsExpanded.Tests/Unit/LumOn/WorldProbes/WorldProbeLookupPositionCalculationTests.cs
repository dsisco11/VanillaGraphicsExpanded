using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks the test-owned arithmetic used to combine relative positions and world origins.</summary>
public sealed class WorldProbeLookupPositionCalculationTests
{
    #region Public API
    /// <summary>Verifies that subtracting the relative origin yields the expected lookup position.</summary>
    [Fact]
    public void LookupPosition_ReturnsExpectedOffsetFromRelativeOrigins()
    {
        // Arrange
        Vec3d surfacePlayerRelative = new(1.25, 0.5, -1.25);
        Vec3d clipmapOriginWorld = new(96.0, 60.0, 96.0);
        Vec3d playerOriginWorld = new(100.0, 64.0, 100.0);
        // Act
        Vec3d lookup = ComputeShaderLookupPosition(
            surfacePlayerRelative,
            clipmapOriginWorld,
            playerOriginWorld);
        // Assert
        Assert.Equal(5.25, lookup.X, 10);
        Assert.Equal(4.5, lookup.Y, 10);
        Assert.Equal(2.75, lookup.Z, 10);
    }

    /// <summary>Verifies repeated inputs produce the same test-helper lookup coordinates.</summary>
    [Fact]
    public void LookupPosition_RepeatedInputsProduceSameCoordinates()
    {
        // Arrange
        Vec3d surfacePlayerRelative = new(1.25, 0.5, -1.25);
        Vec3d clipmapOriginWorld = new(96.0, 60.0, 96.0);
        Vec3d playerOriginWorld = new(100.0, 64.0, 100.0);
        // Act
        Vec3d lookup = ComputeShaderLookupPosition(
            surfacePlayerRelative,
            clipmapOriginWorld,
            playerOriginWorld);
        // Assert
        Assert.Equal(5.25, lookup.X, 10);
        Assert.Equal(4.5, lookup.Y, 10);
        Assert.Equal(2.75, lookup.Z, 10);
    }

    #endregion
    #region Private
    /// <summary>Combines the surface offset with the clipmap origin expressed relative to the player.</summary>
    private static Vec3d ComputeShaderLookupPosition(
        Vec3d surfacePlayerRelative,
        Vec3d clipmapOriginWorld,
        Vec3d playerOriginWorld)
    {
        Vec3d originPlayerRelative = clipmapOriginWorld - playerOriginWorld;
        return surfacePlayerRelative - originPlayerRelative;
    }
    #endregion
}
