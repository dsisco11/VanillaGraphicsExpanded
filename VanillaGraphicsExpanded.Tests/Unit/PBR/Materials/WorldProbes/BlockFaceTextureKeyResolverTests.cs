using System.Collections.Generic;

using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials.WorldProbes;

public sealed class BlockFaceTextureKeyResolverTests
{
    [Fact]
    public void TryResolveBaseTextureLocation_PrefersFaceSpecificThenSideThenAll()
    {
        var block = new Block
        {
            BlockId = 7,
            Code = new AssetLocation("game", "testblock"),
            Textures = new Dictionary<string, CompositeTexture>
            {
                ["all"] = new CompositeTexture(new AssetLocation("game", "block/alltex")),
                ["side"] = new CompositeTexture(new AssetLocation("game", "block/sidetex")),
                ["north"] = new CompositeTexture(new AssetLocation("game", "block/northtex")),
            }
        };

        Assert.True(BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, faceIndex: 0, out AssetLocation tex, out string? from));
        Assert.Equal("north", from);
        Assert.Equal(new AssetLocation("game", "textures/block/northtex.png"), tex);

        Assert.True(BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, faceIndex: 1, out AssetLocation texEast, out string? fromEast));
        Assert.Equal("side", fromEast);
        Assert.Equal(new AssetLocation("game", "textures/block/sidetex.png"), texEast);

        Assert.True(BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, faceIndex: 4, out AssetLocation texUp, out string? fromUp));
        Assert.Equal("all", fromUp);
        Assert.Equal(new AssetLocation("game", "textures/block/alltex.png"), texUp);
    }

    [Fact]
    public void TryResolveBaseTextureLocation_PreservesTexturesPrefixAndExtension()
    {
        var block = new Block
        {
            BlockId = 1,
            Code = new AssetLocation("game", "testblock"),
            Textures = new Dictionary<string, CompositeTexture>
            {
                ["all"] = new CompositeTexture(new AssetLocation("game", "textures/block/foo/bar.png")),
            }
        };

        Assert.True(BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, faceIndex: 2, out AssetLocation tex, out _));
        Assert.Equal(new AssetLocation("game", "textures/block/foo/bar.png"), tex);
    }
}
