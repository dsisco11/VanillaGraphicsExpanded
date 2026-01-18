using System;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal static class MaterialAtlasParamsOverrideApplier
{
    public static void ApplyRgbOverride(
        float[] atlasRgbTriplets,
        int atlasWidth,
        int atlasHeight,
        int rectX,
        int rectY,
        int rectWidth,
        int rectHeight,
        ReadOnlySpan<float> overrideRgba01,
        PbrOverrideScale scale)
    {
        ArgumentNullException.ThrowIfNull(atlasRgbTriplets);

        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
        if (rectX < 0 || rectY < 0) throw new ArgumentOutOfRangeException("rect origin must be non-negative");
        if (rectWidth <= 0 || rectHeight <= 0) throw new ArgumentOutOfRangeException("rect size must be positive");
        if (rectX + rectWidth > atlasWidth || rectY + rectHeight > atlasHeight) throw new ArgumentOutOfRangeException("rect exceeds atlas bounds");

        int expectedAtlasFloats = atlasWidth * atlasHeight * 3;
        if (atlasRgbTriplets.Length != expectedAtlasFloats)
        {
            throw new ArgumentException(
                $"atlasRgbTriplets length {atlasRgbTriplets.Length} does not match atlas size {expectedAtlasFloats} ({atlasWidth}x{atlasHeight}x3)",
                nameof(atlasRgbTriplets));
        }

        int expectedOverrideFloats = rectWidth * rectHeight * 4;
        if (overrideRgba01.Length != expectedOverrideFloats)
        {
            throw new ArgumentException(
                $"overrideRgba01 length {overrideRgba01.Length} does not match rect size {expectedOverrideFloats} ({rectWidth}x{rectHeight}x4)",
                nameof(overrideRgba01));
        }

        float scaleR = scale.Roughness;
        float scaleG = scale.Metallic;
        float scaleB = scale.Emissive;

        bool isIdentity = scaleR == 1f && scaleG == 1f && scaleB == 1f;

        int destStart = (rectY * atlasWidth + rectX) * 3;
        Span<float> destSpan = atlasRgbTriplets.AsSpan(destStart);

        for (int y = 0; y < rectHeight; y++)
        {
            int srcRow = (y * rectWidth) * 4;

            ReadOnlySpan<float> srcRowSpan = overrideRgba01.Slice(srcRow, rectWidth * 4);
            Span<float> dstRowSpan = destSpan.Slice(y * atlasWidth * 3, rectWidth * 3);

            // Channel packing must match vge_material.glsl (RGB = roughness, metallic, emissive). Ignore alpha.
            SimdSpanMath.CopyInterleaved4ToInterleaved3(srcRowSpan, dstRowSpan);
        }

        if (!isIdentity)
        {
            SimdSpanMath.MultiplyClamp01Interleaved3InPlace2D(
                destination3: destSpan,
                rectWidthPixels: rectWidth,
                rectHeightPixels: rectHeight,
                rowStridePixels: atlasWidth,
                mul0: scaleR,
                mul1: scaleG,
                mul2: scaleB);
        }
    }

    public static void ApplyRgbOverride(
        float[] atlasRgbTriplets,
        int atlasWidth,
        int atlasHeight,
        int rectX,
        int rectY,
        int rectWidth,
        int rectHeight,
        ReadOnlySpan<float> overrideRgba01)
    {
        ArgumentNullException.ThrowIfNull(atlasRgbTriplets);

        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
        if (rectX < 0 || rectY < 0) throw new ArgumentOutOfRangeException("rect origin must be non-negative");
        if (rectWidth <= 0 || rectHeight <= 0) throw new ArgumentOutOfRangeException("rect size must be positive");
        if (rectX + rectWidth > atlasWidth || rectY + rectHeight > atlasHeight) throw new ArgumentOutOfRangeException("rect exceeds atlas bounds");

        int expectedAtlasFloats = atlasWidth * atlasHeight * 3;
        if (atlasRgbTriplets.Length != expectedAtlasFloats)
        {
            throw new ArgumentException(
                $"atlasRgbTriplets length {atlasRgbTriplets.Length} does not match atlas size {expectedAtlasFloats} ({atlasWidth}x{atlasHeight}x3)",
                nameof(atlasRgbTriplets));
        }

        int expectedOverrideFloats = rectWidth * rectHeight * 4;
        if (overrideRgba01.Length != expectedOverrideFloats)
        {
            throw new ArgumentException(
                $"overrideRgba01 length {overrideRgba01.Length} does not match rect size {expectedOverrideFloats} ({rectWidth}x{rectHeight}x4)",
                nameof(overrideRgba01));
        }

        for (int y = 0; y < rectHeight; y++)
        {
            int destRow = ((rectY + y) * atlasWidth + rectX) * 3;
            int srcRow = (y * rectWidth) * 4;

            ReadOnlySpan<float> srcRowSpan = overrideRgba01.Slice(srcRow, rectWidth * 4);
            Span<float> dstRowSpan = atlasRgbTriplets.AsSpan(destRow, rectWidth * 3);

            // Channel packing must match vge_material.glsl (RGB = roughness, metallic, emissive). Ignore alpha.
            SimdSpanMath.CopyInterleaved4ToInterleaved3(srcRowSpan, dstRowSpan);
        }
    }


    public static void ApplyRgbOverride(
        float[] atlasRgbTriplets,
        int atlasWidth,
        int atlasHeight,
        int rectX,
        int rectY,
        int rectWidth,
        int rectHeight,
        ReadOnlySpan<byte> overrideRgba)
    {
        ArgumentNullException.ThrowIfNull(atlasRgbTriplets);

        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
        if (rectX < 0 || rectY < 0) throw new ArgumentOutOfRangeException("rect origin must be non-negative");
        if (rectWidth <= 0 || rectHeight <= 0) throw new ArgumentOutOfRangeException("rect size must be positive");
        if (rectX + rectWidth > atlasWidth || rectY + rectHeight > atlasHeight) throw new ArgumentOutOfRangeException("rect exceeds atlas bounds");

        int expectedAtlasFloats = atlasWidth * atlasHeight * 3;
        if (atlasRgbTriplets.Length != expectedAtlasFloats)
        {
            throw new ArgumentException(
                $"atlasRgbTriplets length {atlasRgbTriplets.Length} does not match atlas size {expectedAtlasFloats} ({atlasWidth}x{atlasHeight}x3)",
                nameof(atlasRgbTriplets));
        }

        int expectedOverrideBytes = rectWidth * rectHeight * 4;
        if (overrideRgba.Length != expectedOverrideBytes)
        {
            throw new ArgumentException(
                $"overrideRgba length {overrideRgba.Length} does not match rect size {expectedOverrideBytes} ({rectWidth}x{rectHeight}x4)",
                nameof(overrideRgba));
        }

        for (int y = 0; y < rectHeight; y++)
        {
            int destRow = ((rectY + y) * atlasWidth + rectX) * 3;
            int srcRow = (y * rectWidth) * 4;

            for (int x = 0; x < rectWidth; x++)
            {
                int si = srcRow + (x * 4);
                int di = destRow + (x * 3);

                atlasRgbTriplets[di + 0] = overrideRgba[si + 0] / 255f;
                atlasRgbTriplets[di + 1] = overrideRgba[si + 1] / 255f;
                atlasRgbTriplets[di + 2] = overrideRgba[si + 2] / 255f;
            }
        }
    }
}
