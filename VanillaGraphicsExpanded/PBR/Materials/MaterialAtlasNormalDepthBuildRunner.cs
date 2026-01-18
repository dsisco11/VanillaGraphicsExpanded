using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Render-thread GPU runner for the normal+depth atlas bake.
/// This wrapper centralizes all OpenGL texture writes (bake + override uploads).
/// </summary>
internal sealed class MaterialAtlasNormalDepthBuildRunner
{
    private readonly MaterialAtlasTextureStore textureStore;
    private readonly MaterialOverrideTextureLoader overrideLoader;

    public MaterialAtlasNormalDepthBuildRunner(MaterialAtlasTextureStore textureStore, MaterialOverrideTextureLoader overrideLoader)
    {
        this.textureStore = textureStore ?? throw new ArgumentNullException(nameof(textureStore));
        this.overrideLoader = overrideLoader ?? throw new ArgumentNullException(nameof(overrideLoader));
    }

    public (int bakedRects, int appliedOverrides) ExecutePlan(ICoreClientAPI capi, MaterialAtlasNormalDepthBuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(capi);
        ArgumentNullException.ThrowIfNull(plan);

        int baked = 0;
        int overrides = 0;

        // Group jobs per atlas page for locality.
        var bakeByPage = new Dictionary<int, List<MaterialAtlasNormalDepthBuildPlan.BakeJob>>();
        foreach (var job in plan.BakeJobs)
        {
            if (!bakeByPage.TryGetValue(job.AtlasTextureId, out List<MaterialAtlasNormalDepthBuildPlan.BakeJob>? list))
            {
                list = new List<MaterialAtlasNormalDepthBuildPlan.BakeJob>();
                bakeByPage[job.AtlasTextureId] = list;
            }

            list.Add(job);
        }

        var overridesByPage = new Dictionary<int, List<MaterialAtlasNormalDepthBuildPlan.OverrideJob>>();
        foreach (var ov in plan.OverrideJobs)
        {
            if (!overridesByPage.TryGetValue(ov.AtlasTextureId, out List<MaterialAtlasNormalDepthBuildPlan.OverrideJob>? list))
            {
                list = new List<MaterialAtlasNormalDepthBuildPlan.OverrideJob>();
                overridesByPage[ov.AtlasTextureId] = list;
            }

            list.Add(ov);
        }

        foreach (var page in plan.Pages)
        {
            if (!textureStore.TryGetPageTextures(page.AtlasTextureId, out PbrMaterialAtlasPageTextures pageTextures)
                || pageTextures.NormalDepthTexture is null
                || !pageTextures.NormalDepthTexture.IsValid)
            {
                continue;
            }

            _ = bakeByPage.TryGetValue(page.AtlasTextureId, out List<MaterialAtlasNormalDepthBuildPlan.BakeJob>? bakeJobs);
            _ = overridesByPage.TryGetValue(page.AtlasTextureId, out List<MaterialAtlasNormalDepthBuildPlan.OverrideJob>? overrideJobs);

            // Clear once per page, then bake per rect.
            PbrNormalDepthAtlasGpuBaker.ClearAtlasPage(
                capi,
                destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                atlasWidth: page.Width,
                atlasHeight: page.Height);

            if (bakeJobs is not null)
            {
                foreach (var job in bakeJobs)
                {
                    if (PbrNormalDepthAtlasGpuBaker.BakePerRect(
                        capi,
                        baseAlbedoAtlasPageTexId: page.AtlasTextureId,
                        destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                        atlasWidth: page.Width,
                        atlasHeight: page.Height,
                        rectX: job.Rect.X,
                        rectY: job.Rect.Y,
                        rectWidth: job.Rect.Width,
                        rectHeight: job.Rect.Height,
                        normalScale: job.NormalScale,
                        depthScale: job.DepthScale))
                    {
                        baked++;
                    }
                }
            }

            // Apply explicit overrides after the bake clears and writes into the atlas.
            if (overrideJobs is not null)
            {
                foreach (var ov in overrideJobs)
                {
                    if (!overrideLoader.TryLoadRgbaFloats01(
                            capi,
                            ov.OverrideTexture,
                            out int _,
                            out int _,
                            out float[] rgba01,
                            out string? reason,
                            expectedWidth: ov.Rect.Width,
                            expectedHeight: ov.Rect.Height))
                    {
                        capi.Logger.Warning(
                            "[VGE] PBR override ignored: rule='{0}' target='{1}' override='{2}' reason='{3}'. Falling back to generated maps.",
                            ov.RuleId ?? "(no id)",
                            ov.TargetTexture,
                            ov.OverrideTexture,
                            reason ?? "unknown error");
                        continue;
                    }

                    float normalScale = ov.NormalScale;
                    float depthScale = ov.DepthScale;
                    bool isIdentity = normalScale == 1f && depthScale == 1f;

                    if (isIdentity)
                    {
                        pageTextures.NormalDepthTexture.UploadData(rgba01, ov.Rect.X, ov.Rect.Y, ov.Rect.Width, ov.Rect.Height);
                        overrides++;
                        continue;
                    }

                    int floats = checked(ov.Rect.Width * ov.Rect.Height * 4);

                    // Don't mutate cached arrays from the override loader.
                    var scaled = new float[floats];
                    Array.Copy(rgba01, 0, scaled, 0, floats);

                    // Channel packing must match vge_normaldepth.glsl (RGB = normalXYZ_01, A = height01).
                    SimdSpanMath.MultiplyClamp01Interleaved4InPlace2D(
                        destination4: scaled,
                        rectWidthPixels: ov.Rect.Width,
                        rectHeightPixels: ov.Rect.Height,
                        rowStridePixels: ov.Rect.Width,
                        mulRgb: normalScale,
                        mulA: depthScale);

                    pageTextures.NormalDepthTexture.UploadData(scaled, ov.Rect.X, ov.Rect.Y, ov.Rect.Width, ov.Rect.Height);
                    overrides++;
                }
            }
        }

        return (baked, overrides);
    }
}
