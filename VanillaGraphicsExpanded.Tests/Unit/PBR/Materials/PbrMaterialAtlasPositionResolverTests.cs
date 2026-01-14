using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class PbrMaterialAtlasPositionResolverTests
{
    [Fact]
    public void Resolve_StripsTexturesPrefixAndPngSuffix_ForBlockAtlasKeys()
    {
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

        Assert.True(PbrMaterialAtlasPositionResolver.TryResolve(TryGet, textureAsset, out TextureAtlasPosition? resolved));
        Assert.Same(texPos, resolved);
    }

    [Fact]
    public void NormalizeToAtlasKey_StripsPrefixAndExtension()
    {
        var textureAsset = new AssetLocation("game", "textures/block/test.png");
        AssetLocation normalized = PbrMaterialAtlasPositionResolver.NormalizeToAtlasKey(textureAsset);
        Assert.Equal("game", normalized.Domain);
        Assert.Equal("block/test", normalized.Path);
    }
}
