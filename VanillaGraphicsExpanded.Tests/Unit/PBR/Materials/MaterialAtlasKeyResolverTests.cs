using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks texture path normalization and atlas key lookup against an authored atlas map.</summary>
[Trait("Category", "Unit")]
public sealed class MaterialAtlasKeyResolverTests
{
    #region Public API
    /// <summary>Resolves a texture asset with its directory prefix and file suffix removed from the atlas key.</summary>
    [Fact]
    public void Resolve_StripsTexturesPrefixAndPngSuffix_ForBlockAtlasKeys()
    {
        // Arrange: provide an atlas lookup containing the normalized block texture key.
        var texPos = new TextureAtlasPosition
        {
            atlasTextureId = 123,
            x1 = 0.1f,
            y1 = 0.2f,
            x2 = 0.3f,
            y2 = 0.4f
        };

        var atlas = new Dictionary<string, TextureAtlasPosition>(StringComparer.Ordinal)
        {
            ["game:block/test"] = texPos
        };

        static string Key(AssetLocation loc) => $"{loc.Domain}:{loc.Path}";

        TextureAtlasPosition? TryGet(AssetLocation loc)
        {
            return atlas.TryGetValue(Key(loc), out TextureAtlasPosition? pos) ? pos : null;
        }

        var textureAsset = new AssetLocation("game", "textures/block/test.png");

        // Act: resolve the engine-style texture location through the lookup delegate.
        bool found = MaterialAtlasKeyResolver.TryResolve(TryGet, textureAsset, out TextureAtlasPosition? resolved);

        // Assert: resolution returns the exact atlas entry associated with the canonical key.
        Assert.True(found);
        Assert.Same(texPos, resolved);
    }

    /// <summary>Removes the texture directory prefix and file extension from an atlas key.</summary>
    [Fact]
    public void NormalizeToAtlasKey_StripsPrefixAndExtension()
    {
        // Arrange: use a texture location in the engine's normal asset form.
        var textureAsset = new AssetLocation("game", "textures/block/test.png");

        // Act: derive the key used for atlas lookup.
        AssetLocation normalized = MaterialAtlasKeyResolver.NormalizeToAtlasKey(textureAsset);

        // Assert: the domain remains stable and the path is normalized for lookup.
        Assert.Equal("game", normalized.Domain);
        Assert.Equal("block/test", normalized.Path);
    }

    /// <summary>Recognizes texture prefixes without case sensitivity and removes non-PNG extensions.</summary>
    [Fact]
    public void NormalizeToAtlasKey_StripsAnyExtension_AndIsCaseInsensitiveOnTexturesPrefix()
    {
        // Arrange: provide a mixed-case texture prefix and DDS suffix.
        var textureAsset = new AssetLocation("game", "Textures/block/test.DDS");

        // Act: normalize the location for atlas lookup.
        AssetLocation normalized = MaterialAtlasKeyResolver.NormalizeToAtlasKey(textureAsset);

        // Assert: path normalization is case-insensitive and extension-independent.
        Assert.Equal("game", normalized.Domain);
        Assert.Equal("block/test", normalized.Path);
    }
    #endregion
}
