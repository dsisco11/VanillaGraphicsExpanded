using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks that material encoding changes retire lossy tiles without invalidating normal/depth data.</summary>
[Trait("Category", "Unit")]
public sealed class MaterialCacheEncodingIdentityTests
{
    #region Public API
    /// <summary>Generated and overridden material keys cannot reuse the former BC7 payload identity.</summary>
    [Fact]
    public void MaterialEncodingRetiresLegacyKeysAndPreservesNormalDepthKeys()
    {
        // Arrange: preserve the previous canonical material-key strings as independent legacy identities.
        var builder = new MaterialAtlasCacheKeyBuilder();
        var inputs = new MaterialAtlasCacheKeyInputs(5, "encoding-fixture");
        var rect = new AtlasRect(0, 0, 8, 8);
        var texture = new AssetLocation("game", "textures/block/test.png");
        var replacement = new AssetLocation("game", "textures/block/test_pbr.png");
        var scale = new PbrOverrideScale(1, 1, 1, 1, 1);
        var definition = new PbrMaterialDefinition(.5f, 0, 0, default, scale, 0, null);

        // These canonical strings pin the previous independent payload identities.
        var legacyMaterial = AtlasCacheKey.FromUtf8(5,
            "encoding-fixture|kind=matparams|size=(8,8)|tex=game:textures/block/test.png|def=(0.5,0,0)|noise=(0,0,0,0,0)|scale=(1,1,1,1,1)");
        var legacyOverride = AtlasCacheKey.FromUtf8(5,
            "encoding-fixture|kind=matparams_override|size=(8,8)|target=game:textures/block/test.png|override=game:textures/block/test_pbr.png|rule=(no id)|ruleSrc=(unknown)|scale=(1,1,1)");

        // Act: build the current material and override identities from the same authored inputs.
        AtlasCacheKey currentMaterial = builder.BuildMaterialParamsTileKey(inputs, 1, rect, texture, definition, scale);
        AtlasCacheKey currentOverride = builder.BuildMaterialParamsOverrideTileKey(inputs, 1, rect, texture, replacement, scale, null, null);
        AtlasCacheKey currentNormalDepth = builder.BuildNormalDepthTileKey(inputs, 1, rect, texture, 1, 1);
        AtlasCacheKey currentNormalDepthOverride = builder.BuildNormalDepthOverrideTileKey(inputs, 1, rect, texture, replacement, 1, 1, null, null);

        // Assert: exact RGBA8 material encodings retire keys for the former lossy BC7 payloads.
        Assert.NotEqual(legacyMaterial, currentMaterial);
        Assert.NotEqual(legacyOverride, currentOverride);

        // Assert: normal-depth key formats remain compatible across the material encoding change.
        Assert.Equal(AtlasCacheKey.FromUtf8(5,
            "encoding-fixture|kind=normaldepth|size=(8,8)|tex=game:textures/block/test.png|scale=(1,1)"),
            currentNormalDepth);
        Assert.Equal(AtlasCacheKey.FromUtf8(5,
            "encoding-fixture|kind=normaldepth_override|size=(8,8)|target=game:textures/block/test.png|override=game:textures/block/test_pbr.png|rule=(no id)|ruleSrc=(unknown)|scale=(1,1)"),
            currentNormalDepthOverride);
    }
    #endregion
}
