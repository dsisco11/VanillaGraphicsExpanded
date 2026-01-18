using System;
using System.Threading;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// CPU-side generator for material params atlas tiles (RGB16F: roughness, metallic, emissive).
/// Intended to be used by both sync and async pipelines.
/// </summary>
internal static class MaterialAtlasParamsBuilder
{
    public static float[] BuildRgb16fTile(
        AssetLocation texture,
        PbrMaterialDefinition definition,
        int rectWidth,
        int rectHeight,
        CancellationToken cancellationToken)
        => PbrMaterialParamsPixelBuilder.BuildRgb16fTile(texture, definition, rectWidth, rectHeight, cancellationToken);

    public static void ApplyOverrideToTileRgb16f(
        float[] tileRgbTriplets,
        int rectWidth,
        int rectHeight,
        ReadOnlySpan<float> overrideRgba01,
        PbrOverrideScale scale)
    {
        ArgumentNullException.ThrowIfNull(tileRgbTriplets);

        // Treat the tile as an atlas of the same size and overwrite the full region.
        PbrMaterialParamsOverrideApplier.ApplyRgbOverride(
            atlasRgbTriplets: tileRgbTriplets,
            atlasWidth: rectWidth,
            atlasHeight: rectHeight,
            rectX: 0,
            rectY: 0,
            rectWidth: rectWidth,
            rectHeight: rectHeight,
            overrideRgba01: overrideRgba01,
            scale: scale);
    }
}
