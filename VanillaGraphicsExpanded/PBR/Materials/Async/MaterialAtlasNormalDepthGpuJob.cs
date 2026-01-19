using System;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Profiling;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal readonly record struct MaterialAtlasNormalDepthGpuJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    int AtlasWidth,
    int AtlasHeight,
    MaterialAtlasNormalDepthGpuJob.Kind JobKind,
    float[]? CachedRgbaQuads,
    AssetLocation? TargetTexture,
    AssetLocation? OverrideTexture,
    float NormalScale,
    float DepthScale,
    string? RuleId,
    AssetLocation? RuleSource,
    IMaterialAtlasDiskCache? DiskCache,
    AtlasCacheKey CacheKey,
    int Priority) : IMaterialAtlasGpuJob
{
    internal enum Kind
    {
        UploadCached = 0,
        Bake = 1,
        Override = 2,
    }

    public void Execute(ICoreClientAPI capi, System.Func<int, MaterialAtlasPageTextures?> tryGetPageTextures, MaterialAtlasBuildSession session)
    {
        try
        {
            using var cpuScope = Profiler.BeginScope("MaterialAtlas.NormalDepth.GpuJob", "PBR");

            MaterialAtlasPageTextures? pageTextures = tryGetPageTextures(AtlasTextureId);
            if (pageTextures is null || pageTextures.NormalDepthTexture is null || !pageTextures.NormalDepthTexture.IsValid)
            {
                return;
            }

            session.EnsureNormalDepthPageCleared(capi, AtlasTextureId, pageTextures);

            if (JobKind == Kind.UploadCached)
            {
                float[] cached = CachedRgbaQuads ?? Array.Empty<float>();
                if (cached.Length == checked(Rect.Width * Rect.Height * 4))
                {
                    pageTextures.NormalDepthTexture.UploadData(cached, Rect.X, Rect.Y, Rect.Width, Rect.Height);
                }

                return;
            }

            if (JobKind == Kind.Override)
            {
                if (OverrideTexture is null)
                {
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
                    if (TargetTexture is not null)
                    {
                        session.LogOverrideFailureOnce(
                            capi,
                            RuleId,
                            TargetTexture,
                            OverrideTexture,
                            reason);
                    }
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

                TryQueueCacheWrite(session, uploadData);
                return;
            }

            // Bake
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

            if (DiskCache is null || CacheKey.SchemaVersion == 0 || session.IsCancelled || session.GenerationId != GenerationId)
            {
                return;
            }

            // Readback must happen on the render thread, but disk IO does not.
            float[] rgbaQuads = pageTextures.NormalDepthTexture.ReadPixelsRegion(Rect.X, Rect.Y, Rect.Width, Rect.Height);
            if (rgbaQuads.Length != checked(Rect.Width * Rect.Height * 4))
            {
                return;
            }

            TryQueueCacheWrite(session, rgbaQuads);
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

    private void TryQueueCacheWrite(MaterialAtlasBuildSession session, float[] rgbaQuads)
    {
        if (DiskCache is null || CacheKey.SchemaVersion == 0)
        {
            return;
        }

        int genId = GenerationId;

        if (session.IsCancelled || session.GenerationId != genId)
        {
            return;
        }

        IMaterialAtlasDiskCache diskCache = DiskCache;
        AtlasCacheKey key = CacheKey;
        int w = Rect.Width;
        int h = Rect.Height;

        _ = MaterialAtlasDiskCacheIoQueue.TryQueue(() =>
        {
            if (session.IsCancelled || session.GenerationId != genId)
            {
                return;
            }

            diskCache.StoreNormalDepthTile(key, w, h, rgbaQuads);
        });
    }
}
