using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// CPU job: reads a cached material params tile from disk and emits a GPU upload.
/// Cache misses are treated as a no-op so the normal pipeline can regenerate later.
/// </summary>
internal readonly record struct MaterialAtlasParamsCpuDiskCacheOnlyJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    AssetLocation TargetTexture,
    IMaterialAtlasDiskCache DiskCache,
    AtlasCacheKey CacheKey,
    int Priority) : IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>
{
    public MaterialAtlasParamsGpuTileUpload Execute(CancellationToken cancellationToken)
    {
        if (CacheKey.SchemaVersion != 0 && DiskCache.TryLoadMaterialParamsTile(CacheKey, out float[] rgbTriplets))
        {
            return new MaterialAtlasParamsGpuTileUpload(
                GenerationId,
                AtlasTextureId,
                Rect,
                rgbTriplets,
                TargetTexture,
                Priority,
                SkipUpload: false,
                SuppressOverrideUpload: false);
        }

        return new MaterialAtlasParamsGpuTileUpload(
            GenerationId,
            AtlasTextureId,
            Rect,
            System.Array.Empty<float>(),
            TargetTexture,
            Priority,
            SkipUpload: true,
            SuppressOverrideUpload: false);
    }
}
