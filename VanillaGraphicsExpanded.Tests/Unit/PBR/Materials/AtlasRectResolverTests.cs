using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class AtlasRectResolverTests
{
    [Fact]
    public void TryResolvePixelRect_ReturnsFalse_ForNullPosition()
    {
        Assert.False(AtlasRectResolver.TryResolvePixelRect(null, atlasWidth: 16, atlasHeight: 16, out _));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(-1, 16)]
    [InlineData(16, -1)]
    public void TryResolvePixelRect_ReturnsFalse_ForInvalidAtlasSize(int w, int h)
    {
        var pos = new TextureAtlasPosition { x1 = 0f, y1 = 0f, x2 = 1f, y2 = 1f };
        Assert.False(AtlasRectResolver.TryResolvePixelRect(pos, w, h, out _));
    }

    [Fact]
    public void TryResolvePixelRect_ClampsAndRounds_UsingFloorForMinAndCeilForMax()
    {
        // Chosen so that floor/ceil differences are visible.
        // width=10 => x1=-0.1 -> floor(-1) -> clamp to 0
        //            x2=1.0 -> ceil(10) -> clamp to 10
        // height=8 => y1=0.125 -> floor(1) -> 1
        //            y2=0.25 -> ceil(2) -> 2
        var pos = new TextureAtlasPosition { x1 = -0.1f, y1 = 0.125f, x2 = 1.0f, y2 = 0.25f };

        Assert.True(AtlasRectResolver.TryResolvePixelRect(pos, atlasWidth: 10, atlasHeight: 8, out AtlasRect rect));
        Assert.Equal(0, rect.X);
        Assert.Equal(1, rect.Y);
        Assert.Equal(10, rect.Width);
        Assert.Equal(1, rect.Height);
        Assert.Equal(10, rect.Right);
        Assert.Equal(2, rect.Bottom);
    }

    [Fact]
    public void TryResolvePixelRect_ReturnsFalse_WhenResultIsEmpty()
    {
        // x1 == x2 after conversion => zero width.
        var pos = new TextureAtlasPosition { x1 = 0.9f, y1 = 0f, x2 = 0.9f, y2 = 1f };

        Assert.False(AtlasRectResolver.TryResolvePixelRect(pos, atlasWidth: 10, atlasHeight: 10, out _));
    }
}
