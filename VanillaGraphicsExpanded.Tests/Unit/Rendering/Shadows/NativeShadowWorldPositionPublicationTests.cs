using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Checks native light publication retains captured world coordinates independently from shader view positions.</summary>
public sealed class NativeShadowWorldPositionPublicationTests
{
    #region Public API
    /// <summary>Large signed native positions remain exact when the same light payload is camera-relative.</summary>
    [Theory]
    [InlineData(3_950_617_283.25, -7_506_172_480.5, 1_106_172_486.125)]
    [InlineData(-3_950_617_283.25, 7_506_172_480.5, -1_106_172_486.125)]
    public void CaptureRetainsExplicitWorldPositionBesideRelativeShaderPosition(double worldX, double worldY, double worldZ)
    {
        // Arrange: the source is far from the origin while its visible shader position is small and relative.
        using var buffer = new VgeLightsUniformBuffer();
        var source = new ShadowSourceKey(1, 0, ShadowSourceKind.Block, 2, 3, 0);
        var worldPosition = new Vector3d(worldX, worldY, worldZ);

        // Act: publish independently captured absolute coordinates with the existing compact view-space lighting payload.
        buffer.Capture(1, [17.25f, -4.5f, 8.125f], [1, 2, 3], [source], [worldPosition]);

        // Assert: shadow ownership uses the exact world point while visible lighting retains its original relative input.
        var point = Assert.Single(buffer.Lights);
        Assert.Equal(worldPosition, point.WorldPosition!.Value);
        Assert.Equal(new Vector3(17.25f, -4.5f, 8.125f), point.ViewPosition);
        Assert.Equal(source, point.Source);
        Assert.Equal(new Vector4(1, 2, 3, 0), point.Payload);
    }

    /// <summary>Illumination-only entries remain available without inventing a shadow position.</summary>
    [Fact]
    public void MissingWorldPositionRetainsIlluminationWithoutShadowGeometryPosition()
    {
        // Arrange: capture an existing shader-space light without native shadow-source metadata.
        using var buffer = new VgeLightsUniformBuffer();

        // Act: omit source identities and absolute positions.
        buffer.Capture(1, [2, 3, 4], [8, 7, 6]);

        // Assert: the source is unknown and has no fabricated absolute shadow coordinate.
        var point = Assert.Single(buffer.Lights);
        Assert.False(point.Source.IsKnown);
        Assert.Null(point.WorldPosition);
        Assert.Equal(new Vector3(2, 3, 4), point.ViewPosition);
        Assert.Equal(new Vector4(8, 7, 6, 0), point.Payload);
    }
    #endregion
}
