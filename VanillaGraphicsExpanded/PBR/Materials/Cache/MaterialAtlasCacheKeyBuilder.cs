using System.Globalization;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// Deterministic cache key builder for per-tile atlas outputs.
/// </summary>
internal sealed class MaterialAtlasCacheKeyBuilder
{
    public AtlasCacheKey BuildMaterialParamsTileKey(
        in MaterialAtlasCacheKeyInputs inputs,
        int atlasTextureId,
        AtlasRect rect,
        AssetLocation texture,
        PbrMaterialDefinition definition,
        PbrOverrideScale scale)
    {
        string stableKey = string.Format(
            CultureInfo.InvariantCulture,
            "{0}|kind=matparams|size=({1},{2})|tex={3}|def=({4:R},{5:R},{6:R})|noise=({7:R},{8:R},{9:R},{10:R},{11:R})|scale=({12:R},{13:R},{14:R},{15:R},{16:R})",
            inputs.StablePrefix,
            rect.Width,
            rect.Height,
            texture,
            definition.Roughness,
            definition.Metallic,
            definition.Emissive,
            definition.Noise.Roughness,
            definition.Noise.Metallic,
            definition.Noise.Emissive,
            definition.Noise.Reflectivity,
            definition.Noise.Normals,
            scale.Roughness,
            scale.Metallic,
            scale.Emissive,
            scale.Normal,
            scale.Depth);

        return AtlasCacheKey.FromUtf8(inputs.SchemaVersion, stableKey);
    }

    public AtlasCacheKey BuildMaterialParamsOverrideTileKey(
        in MaterialAtlasCacheKeyInputs inputs,
        int atlasTextureId,
        AtlasRect rect,
        AssetLocation targetTexture,
        AssetLocation overrideTexture,
        PbrOverrideScale overrideScale,
        string? ruleId,
        AssetLocation? ruleSource)
    {
        string stableKey = string.Format(
            CultureInfo.InvariantCulture,
            "{0}|kind=matparams_override|size=({1},{2})|target={3}|override={4}|rule={5}|ruleSrc={6}|scale=({7:R},{8:R},{9:R})",
            inputs.StablePrefix,
            rect.Width,
            rect.Height,
            targetTexture,
            overrideTexture,
            ruleId ?? "(no id)",
            ruleSource?.ToString() ?? "(unknown)",
            overrideScale.Roughness,
            overrideScale.Metallic,
            overrideScale.Emissive);

        return AtlasCacheKey.FromUtf8(inputs.SchemaVersion, stableKey);
    }

    public AtlasCacheKey BuildNormalDepthTileKey(
        in MaterialAtlasCacheKeyInputs inputs,
        int atlasTextureId,
        AtlasRect rect,
        AssetLocation? texture,
        float normalScale,
        float depthScale)
    {
        string stableKey = string.Format(
            CultureInfo.InvariantCulture,
            "{0}|kind=normaldepth|size=({1},{2})|tex={3}|scale=({4:R},{5:R})",
            inputs.StablePrefix,
            rect.Width,
            rect.Height,
            texture?.ToString() ?? "(unknown)",
            normalScale,
            depthScale);

        return AtlasCacheKey.FromUtf8(inputs.SchemaVersion, stableKey);
    }

    public AtlasCacheKey BuildNormalDepthOverrideTileKey(
        in MaterialAtlasCacheKeyInputs inputs,
        int atlasTextureId,
        AtlasRect rect,
        AssetLocation targetTexture,
        AssetLocation overrideTexture,
        float normalScale,
        float depthScale,
        string? ruleId,
        AssetLocation? ruleSource)
    {
        string stableKey = string.Format(
            CultureInfo.InvariantCulture,
            "{0}|kind=normaldepth_override|size=({1},{2})|target={3}|override={4}|rule={5}|ruleSrc={6}|scale=({7:R},{8:R})",
            inputs.StablePrefix,
            rect.Width,
            rect.Height,
            targetTexture,
            overrideTexture,
            ruleId ?? "(no id)",
            ruleSource?.ToString() ?? "(unknown)",
            normalScale,
            depthScale);

        return AtlasCacheKey.FromUtf8(inputs.SchemaVersion, stableKey);
    }
}
