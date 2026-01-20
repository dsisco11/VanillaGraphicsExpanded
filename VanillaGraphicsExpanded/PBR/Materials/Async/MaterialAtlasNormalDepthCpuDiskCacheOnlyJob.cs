using System;
using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// CPU job: reads a cached normal+depth tile from disk and emits a GPU upload.
/// Cache misses are treated as a no-op so the normal pipeline can regenerate later.
/// </summary>
internal readonly record struct MaterialAtlasNormalDepthCpuDiskCacheOnlyJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    int AtlasWidth,
    int AtlasHeight,
    AssetLocation? TargetTexture,
    AssetLocation? OverrideTexture,
    float NormalScale,
    float DepthScale,
    string? RuleId,
    AssetLocation? RuleSource,
    IMaterialAtlasDiskCache DiskCache,
    AtlasCacheKey CacheKey,
    int Priority) : IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob>
{
    public MaterialAtlasNormalDepthGpuJob Execute(CancellationToken cancellationToken)
    {
        float[] cached = Array.Empty<float>();

        if (CacheKey.SchemaVersion != 0)
        {
            _ = DiskCache.TryLoadNormalDepthTile(CacheKey, out cached);
        }

        return new MaterialAtlasNormalDepthGpuJob(
            GenerationId: GenerationId,
            AtlasTextureId: AtlasTextureId,
            Rect: Rect,
            AtlasWidth: AtlasWidth,
            AtlasHeight: AtlasHeight,
            JobKind: MaterialAtlasNormalDepthGpuJob.Kind.UploadCached,
            CachedRgbaQuads: cached,
            TargetTexture: TargetTexture,
            OverrideTexture: OverrideTexture,
            NormalScale: NormalScale,
            DepthScale: DepthScale,
            RuleId: RuleId,
            RuleSource: RuleSource,
            DiskCache: DiskCache,
            CacheKey: CacheKey,
            Priority: Priority);
    }
}
