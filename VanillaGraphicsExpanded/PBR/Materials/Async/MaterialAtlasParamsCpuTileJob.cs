using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// CPU job: build procedural material params for a tile.
/// </summary>
internal readonly record struct MaterialAtlasParamsCpuTileJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    AssetLocation Texture,
    PbrMaterialDefinition? Definition,
    int Priority,
    IMaterialAtlasDiskCache? DiskCache,
    AtlasCacheKey BaseCacheKey,
    AtlasCacheKey OverrideCacheKey,
    bool HasOverride,
    bool IsOverrideOnly,
    MaterialAtlasAsyncCacheCounters? CacheCounters) : IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>
{
    public MaterialAtlasParamsGpuTileUpload Execute(CancellationToken cancellationToken)
    {
        if (DiskCache is not null)
        {
            // Prefer cached post-override output so we can skip both the procedural build and the override stage.
            if (HasOverride && OverrideCacheKey.SchemaVersion != 0
                && DiskCache.TryLoadMaterialParamsTile(OverrideCacheKey, out float[] cachedOverrideRgb))
            {
                CacheCounters?.IncrementOverrideHit();
                return new MaterialAtlasParamsGpuTileUpload(
                    GenerationId,
                    AtlasTextureId,
                    Rect,
                    cachedOverrideRgb,
                    Texture,
                    Priority,
                    SkipUpload: false,
                    SuppressOverrideUpload: true);
            }

            if (HasOverride)
            {
                CacheCounters?.IncrementOverrideMiss();
            }

            if (!IsOverrideOnly && BaseCacheKey.SchemaVersion != 0
                && DiskCache.TryLoadMaterialParamsTile(BaseCacheKey, out float[] cachedRgb))
            {
                CacheCounters?.IncrementBaseHit();
                return new MaterialAtlasParamsGpuTileUpload(
                    GenerationId,
                    AtlasTextureId,
                    Rect,
                    cachedRgb,
                    Texture,
                    Priority,
                    SkipUpload: false,
                    SuppressOverrideUpload: false);
            }

            if (!IsOverrideOnly)
            {
                CacheCounters?.IncrementBaseMiss();
            }
        }

        // Override-only rects have no corresponding procedural tile job; keep existing behavior:
        // don't overwrite defaults, but trigger the override upload stage.
        if (IsOverrideOnly)
        {
            return new MaterialAtlasParamsGpuTileUpload(
                GenerationId: GenerationId,
                AtlasTextureId: AtlasTextureId,
                Rect: Rect,
                RgbTriplets: System.Array.Empty<float>(),
                TargetTexture: Texture,
                Priority: Priority,
                SkipUpload: true,
                SuppressOverrideUpload: false);
        }

        PbrMaterialDefinition def = Definition ?? throw new System.InvalidOperationException("Material definition is required for procedural tiles.");

        float[] rgb = MaterialAtlasParamsBuilder.BuildRgb16fTile(
            Texture,
            def,
            rectWidth: Rect.Width,
            rectHeight: Rect.Height,
            cancellationToken);

        DiskCache?.StoreMaterialParamsTile(BaseCacheKey, Rect.Width, Rect.Height, rgb);

        return new MaterialAtlasParamsGpuTileUpload(
            GenerationId,
            AtlasTextureId,
            Rect,
            rgb,
            Texture,
            Priority,
            SkipUpload: false,
            SuppressOverrideUpload: false);
    }
}
