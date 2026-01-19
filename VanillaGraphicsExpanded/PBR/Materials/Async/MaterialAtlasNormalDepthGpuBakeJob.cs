using System;

using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Profiling;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal readonly record struct MaterialAtlasNormalDepthGpuBakeJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    int AtlasWidth,
    int AtlasHeight,
    float NormalScale,
    float DepthScale,
    IMaterialAtlasDiskCache? DiskCache,
    AtlasCacheKey CacheKey,
    int Priority) : IMaterialAtlasGpuJob
{
    public void Execute(ICoreClientAPI capi, Func<int, MaterialAtlasPageTextures?> tryGetPageTextures, MaterialAtlasBuildSession session)
    {
        try
        {
            using var cpuScope = Profiler.BeginScope("MaterialAtlas.NormalDepth.Job.Bake", "PBR");

            MaterialAtlasPageTextures? pageTextures = tryGetPageTextures(AtlasTextureId);
            if (pageTextures is null
                || pageTextures.NormalDepthTexture is null
                || !pageTextures.NormalDepthTexture.IsValid)
            {
                return;
            }

            session.EnsureNormalDepthPageCleared(capi, AtlasTextureId, pageTextures);

            if (DiskCache is not null && CacheKey.SchemaVersion != 0
                && DiskCache.TryLoadNormalDepthTile(CacheKey, out float[] cached)
                && cached.Length == checked(Rect.Width * Rect.Height * 4))
            {
                pageTextures.NormalDepthTexture.UploadData(cached, Rect.X, Rect.Y, Rect.Width, Rect.Height);
                return;
            }

            _ = MaterialAtlasNormalDepthGpuBuilder.BakePerRect(
                capi,
                baseAlbedoAtlasPageTexId: AtlasTextureId,
                destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                atlasWidth: AtlasWidth,
                atlasHeight: AtlasHeight,
                rectX: Rect.X,
                rectY: Rect.Y,
                rectWidth: Rect.Width,
                rectHeight: Rect.Height,
                normalScale: NormalScale,
                depthScale: DepthScale);

            if (DiskCache is not null && CacheKey.SchemaVersion != 0)
            {
                try
                {
                    float[] rgbaQuads = pageTextures.NormalDepthTexture.ReadPixelsRegion(Rect.X, Rect.Y, Rect.Width, Rect.Height);
                    if (rgbaQuads.Length == checked(Rect.Width * Rect.Height * 4))
                    {
                        DiskCache.StoreNormalDepthTile(CacheKey, Rect.Width, Rect.Height, rgbaQuads);
                    }
                }
                catch
                {
                    // Best-effort.
                }
            }
        }
        catch
        {
            // Best-effort: avoid stalling the async scheduler.
        }
        finally
        {
            session.MarkNormalDepthJobCompleted(AtlasTextureId);
        }
    }
}
