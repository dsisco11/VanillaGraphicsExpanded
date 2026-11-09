using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks probe hit face util behavior and contract rules.</summary>
public sealed class ProbeHitFaceUtilTests
{
    #region Public API
    /// <summary>Verifies the from axis normal maps canonical axes to faces behavior.</summary>
    [Theory]
    [InlineData(0, 0, -1, 0)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(0, 0, 1, 2)]
    [InlineData(-1, 0, 0, 3)]
    [InlineData(0, 1, 0, 4)]
    [InlineData(0, -1, 0, 5)]
    public void FromAxisNormal_MapsCanonicalAxesToFaces(int x, int y, int z, byte expectedFaceIndex)
    {
        // Arrange
        var n = new VectorInt3(x, y, z);
        // Act
        ProbeHitFace face = ProbeHitFaceUtil.FromAxisNormal(n);
        // Assert
        Assert.Equal(expectedFaceIndex, (byte)face);
    }
    #endregion
}











