using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks normalized atlas positions become non-empty pixel rectangles with bounded edges.</summary>
[Trait("Category", "Unit")]
public sealed class AtlasRectResolverTests
{
    #region Public API
    /// <summary>Rejects an absent atlas position without producing a rectangle.</summary>
    [Fact]
    public void TryResolvePixelRect_ReturnsFalse_ForNullPosition()
    {
        // Arrange: the atlas dimensions are valid, but no position was supplied.

        // Act: try to resolve a rectangle without a position.
        bool resolved = AtlasRectResolver.TryResolvePixelRect(null, atlasWidth: 16, atlasHeight: 16, out _);

        // Assert: a missing position cannot be resolved.
        Assert.False(resolved);
    }

    /// <summary>Rejects atlas dimensions that cannot represent pixel coordinates.</summary>
    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(-1, 16)]
    [InlineData(16, -1)]
    public void TryResolvePixelRect_ReturnsFalse_ForInvalidAtlasSize(int w, int h)
    {
        // Arrange: a full-atlas position cannot make a non-positive atlas valid.
        var pos = new TextureAtlasPosition { x1 = 0f, y1 = 0f, x2 = 1f, y2 = 1f };

        // Act: try to resolve the position with these atlas dimensions.
        bool resolved = AtlasRectResolver.TryResolvePixelRect(pos, w, h, out _);

        // Assert: invalid dimensions are rejected.
        Assert.False(resolved);
    }

    /// <summary>Floors minimum edges, ceils maximum edges, and clamps the result to the atlas.</summary>
    [Fact]
    public void TryResolvePixelRect_ClampsAndRounds_UsingFloorForMinAndCeilForMax()
    {
        // Arrange: fractional vertical edges distinguish floor from ceil, while horizontal edges exceed atlas bounds.
        var pos = new TextureAtlasPosition { x1 = -0.14f, y1 = 0.13f, x2 = 1.06f, y2 = 0.26f };

        // Act: convert normalized coordinates to a pixel rectangle.
        bool resolved = AtlasRectResolver.TryResolvePixelRect(pos, atlasWidth: 10, atlasHeight: 8, out AtlasRect rect);

        // Assert: fractional edges round outward and out-of-range horizontal edges clamp to atlas bounds.
        Assert.True(resolved);
        Assert.Equal(0, rect.X);
        Assert.Equal(1, rect.Y);
        Assert.Equal(10, rect.Width);
        Assert.Equal(2, rect.Height);
        Assert.Equal(10, rect.Right);
        Assert.Equal(3, rect.Bottom);
    }

    /// <summary>Rejects positions that collapse to an empty pixel rectangle.</summary>
    [Fact]
    public void TryResolvePixelRect_ReturnsFalse_WhenResultIsEmpty()
    {
        // Arrange: equal horizontal edges remain equal after conversion.
        var pos = new TextureAtlasPosition { x1 = 0.9f, y1 = 0f, x2 = 0.9f, y2 = 1f };

        // Act: try to resolve the equal horizontal edges.
        bool resolved = AtlasRectResolver.TryResolvePixelRect(pos, atlasWidth: 10, atlasHeight: 10, out _);

        // Assert: zero-width positions are not usable rectangles.
        Assert.False(resolved);
    }
    #endregion
}
