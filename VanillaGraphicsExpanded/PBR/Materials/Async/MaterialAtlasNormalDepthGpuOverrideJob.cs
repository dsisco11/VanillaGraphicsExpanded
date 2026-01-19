using System;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Profiling;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal readonly record struct MaterialAtlasNormalDepthGpuOverrideJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    int AtlasWidth,
    int AtlasHeight,
    AssetLocation TargetTexture,
    AssetLocation OverrideTexture,
    float NormalScale,
    float DepthScale,
    string? RuleId,
    AssetLocation? RuleSource,
    IMaterialAtlasDiskCache? DiskCache,
    AtlasCacheKey CacheKey,
    int Priority) : IMaterialAtlasGpuJob
{
    public void Execute(ICoreClientAPI capi, System.Func<int, MaterialAtlasPageTextures?> tryGetPageTextures, MaterialAtlasBuildSession session)
    {
        try
        {
            using var cpuScope = Profiler.BeginScope("MaterialAtlas.NormalDepth.Job.Override", "PBR");

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

            var loader = session.OverrideLoader;
            if (!loader.TryLoadRgbaFloats01(
                    capi,
                    OverrideTexture,
                    out int _,
                    out int _,
                    out float[] rgba01,
                    out string? reason,
                    expectedWidth: Rect.Width,
                    expectedHeight: Rect.Height))
            {
                session.LogOverrideFailureOnce(
                    capi,
                    RuleId,
                    TargetTexture,
                    OverrideTexture,
                    reason);
                return;
            }

            bool isIdentity = NormalScale == 1f && DepthScale == 1f;
            float[] uploadData = rgba01;

            if (!isIdentity)
            {
                int floats = checked(Rect.Width * Rect.Height * 4);
                var scaled = new float[floats];
                Array.Copy(rgba01, 0, scaled, 0, floats);

                SimdSpanMath.MultiplyClamp01Interleaved4InPlace2D(
                    destination4: scaled,
                    rectWidthPixels: Rect.Width,
                    rectHeightPixels: Rect.Height,
                    rowStridePixels: Rect.Width,
                    mulRgb: NormalScale,
                    mulA: DepthScale);

                uploadData = scaled;
            }

            pageTextures.NormalDepthTexture.UploadData(uploadData, Rect.X, Rect.Y, Rect.Width, Rect.Height);

            if (DiskCache is not null && CacheKey.SchemaVersion != 0 && !session.IsCancelled && session.GenerationId == GenerationId)
            {
                IMaterialAtlasDiskCache diskCache = DiskCache;
                AtlasCacheKey cacheKey = CacheKey;
                int w = Rect.Width;
                int h = Rect.Height;
                float[] copy = uploadData;

                // Never block the render thread on IO.
                _ = Task.Run(() =>
                {
                    try
                    {
                        diskCache.StoreNormalDepthTile(cacheKey, w, h, copy);
                    }
                    catch
                    {
                        // Best-effort.
                    }
                });
            }
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            session.MarkNormalDepthJobCompleted(AtlasTextureId);
        }
    }
}
