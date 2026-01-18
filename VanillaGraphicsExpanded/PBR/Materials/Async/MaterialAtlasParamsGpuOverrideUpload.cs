using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// GPU job: apply a material params override (loaded from assets) to a rect.
/// </summary>
internal readonly record struct MaterialAtlasParamsGpuOverrideUpload(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    AssetLocation TargetTexture,
    AssetLocation OverrideAsset,
    string? RuleId,
    AssetLocation? RuleSource,
    PbrOverrideScale Scale,
    int Priority) : IMaterialAtlasGpuJob
{
    public void Execute(ICoreClientAPI capi, System.Func<int, PbrMaterialAtlasPageTextures?> tryGetPageTextures, PbrMaterialAtlasBuildSession session)
    {
        var pageTextures = tryGetPageTextures(AtlasTextureId);
        if (pageTextures is null)
        {
            return;
        }

        var loader = session.OverrideLoader;
        if (!loader.TryLoadRgbaFloats01(
                capi,
                OverrideAsset,
                out int _,
                out int _,
                out float[] rgba01,
                out string? reason,
                expectedWidth: Rect.Width,
                expectedHeight: Rect.Height))
        {
            // Warn once per rule+target.
            session.LogOverrideFailureOnce(
                capi,
                RuleId,
                TargetTexture,
                OverrideAsset,
                reason);
            return;
        }

        float[] rgb = new float[checked(Rect.Width * Rect.Height * 3)];
        for (int y = 0; y < Rect.Height; y++)
        {
            int srcRow = (y * Rect.Width) * 4;
            int dstRow = (y * Rect.Width) * 3;

            ReadOnlySpan<float> src = rgba01.AsSpan(srcRow, Rect.Width * 4);
            Span<float> dst = rgb.AsSpan(dstRow, Rect.Width * 3);

            // Channel packing must match vge_material.glsl (RGB = roughness, metallic, emissive). Ignore alpha.
            SimdSpanMath.CopyInterleaved4ToInterleaved3(src, dst);
        }

        if (Scale.Roughness != 1f || Scale.Metallic != 1f || Scale.Emissive != 1f)
        {
            SimdSpanMath.MultiplyClamp01Interleaved3InPlace2D(
                destination3: rgb,
                rectWidthPixels: Rect.Width,
                rectHeightPixels: Rect.Height,
                rowStridePixels: Rect.Width,
                mul0: Scale.Roughness,
                mul1: Scale.Metallic,
                mul2: Scale.Emissive);
        }

        pageTextures.MaterialParamsTexture.UploadData(rgb, Rect.X, Rect.Y, Rect.Width, Rect.Height);
        session.IncrementOverridesApplied();

        if (session.PagesByAtlasTexId.TryGetValue(AtlasTextureId, out PbrMaterialAtlasPageBuildState? page))
        {
            page.PendingOverrides = System.Math.Max(0, page.PendingOverrides - 1);
            page.CompletedOverrides++;
        }
    }
}
