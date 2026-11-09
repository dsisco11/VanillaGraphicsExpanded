using System.Collections.Generic;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials.WorldProbes;

/// <summary>Verifies block-face texture precedence and normalized texture key construction.</summary>
public sealed class BlockFaceTextureKeyResolverTests
{
    #region Public API
    /// <summary>Resolver prefers face-specific texture, then side texture, then the all-face texture.</summary>
    [Fact]
    public void TryResolveBaseTextureLocation_PrefersFaceSpecificThenSideThenAll()
    {
        // Arrange: define all, side, and north-face texture candidates on one block.
        var block = new Block
        {
            BlockId = 7,
            Code = new AssetLocation("game", "testblock"),
            Textures = new Dictionary<string, CompositeTexture>
            {
                ["all"] = new CompositeTexture(new AssetLocation("game", "block/alltex")),
                ["side"] = new CompositeTexture(new AssetLocation("game", "block/sidetex")),
                ["north"] = new CompositeTexture(new AssetLocation("game", "block/northtex"))
            }
        };

        // Act and assert: north selects the exact face entry and east/up fall back by precedence.
        Assert.True(BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, 0, out AssetLocation north, out string? northSource));
        Assert.Equal("north", northSource);
        Assert.Equal(new AssetLocation("game", "textures/block/northtex"), north);
        Assert.True(BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, 1, out AssetLocation east, out string? eastSource));
        Assert.Equal("side", eastSource);
        Assert.Equal(new AssetLocation("game", "textures/block/sidetex"), east);
        Assert.True(BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, 4, out AssetLocation up, out string? upSource));
        Assert.Equal("all", upSource);
        Assert.Equal(new AssetLocation("game", "textures/block/alltex"), up);
    }

    /// <summary>Resolver retains an existing textures prefix while removing the source extension.</summary>
    [Fact]
    public void TryResolveBaseTextureLocation_PreservesTexturesPrefix_AndStripsExtension()
    {
        // Arrange: use an all-faces path that already contains the standard prefix and a file extension.
        var block = new Block
        {
            BlockId = 1,
            Code = new AssetLocation("game", "testblock"),
            Textures = new Dictionary<string, CompositeTexture>
            {
                ["all"] = new CompositeTexture(new AssetLocation("game", "textures/block/foo/bar.png"))
            }
        };

        // Act: resolve a face that has no more-specific entry.
        bool resolved = BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, 2,
            out AssetLocation texture, out _);

        // Assert: normalization does not duplicate the prefix and strips only the extension.
        Assert.True(resolved);
        Assert.Equal(new AssetLocation("game", "textures/block/foo/bar"), texture);
    }
    #endregion
}
