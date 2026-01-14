using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal static class PbrMaterialParamsPixelBuilder
{
    public readonly record struct Result(
        Dictionary<int, float[]> PixelBuffersByAtlasTexId,
        Dictionary<int, (int width, int height)> SizesByAtlasTexId,
        int FilledRects);

    // Defaults for unmapped textures.
    internal const float DefaultRoughness = 0.85f;
    internal const float DefaultMetallic = 0.0f;
    internal const float DefaultEmissive = 0.0f;

    public static Result BuildRgb16fPixelBuffers(
        IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages,
        IReadOnlyDictionary<AssetLocation, TextureAtlasPosition> texturePositions,
        IReadOnlyDictionary<AssetLocation, PbrMaterialDefinition> materialsByTexture)
    {
        if (atlasPages is null) throw new ArgumentNullException(nameof(atlasPages));
        if (texturePositions is null) throw new ArgumentNullException(nameof(texturePositions));
        if (materialsByTexture is null) throw new ArgumentNullException(nameof(materialsByTexture));

        var pixelBuffersByAtlasTexId = new Dictionary<int, float[]>(capacity: atlasPages.Count);
        var sizesByAtlasTexId = new Dictionary<int, (int width, int height)>(capacity: atlasPages.Count);

        foreach ((int atlasTextureId, int width, int height) in atlasPages)
        {
            if (atlasTextureId == 0 || width <= 0 || height <= 0)
            {
                continue;
            }

            sizesByAtlasTexId[atlasTextureId] = (width, height);

            float[] pixels = new float[width * height * 3];
            FillRgbTriplets(pixels, DefaultRoughness, DefaultMetallic, DefaultEmissive);

            pixelBuffersByAtlasTexId[atlasTextureId] = pixels;
        }

        int filledRects = 0;
        foreach ((AssetLocation texture, PbrMaterialDefinition definition) in materialsByTexture)
        {
            if (!texturePositions.TryGetValue(texture, out TextureAtlasPosition? texPos) || texPos is null)
            {
                continue;
            }

            if (!pixelBuffersByAtlasTexId.TryGetValue(texPos.atlasTextureId, out float[]? pixels))
            {
                // Not in block atlas (or atlas page not tracked)
                continue;
            }

            (int width, int height) = sizesByAtlasTexId[texPos.atlasTextureId];

            // TextureAtlasPosition coordinates are normalized (0..1) in atlas UV space.
            // Convert to integer pixel bounds.
            int x1 = Clamp((int)Math.Floor(texPos.x1 * width), 0, width - 1);
            int y1 = Clamp((int)Math.Floor(texPos.y1 * height), 0, height - 1);
            int x2 = Clamp((int)Math.Ceiling(texPos.x2 * width), 0, width);
            int y2 = Clamp((int)Math.Ceiling(texPos.y2 * height), 0, height);

            int rectWidth = x2 - x1;
            int rectHeight = y2 - y1;
            if (rectWidth <= 0 || rectHeight <= 0)
            {
                continue;
            }

            float roughness = Clamp01(definition.Roughness);
            float metallic = Clamp01(definition.Metallic);
            float emissive = Clamp01(definition.Emissive);

            for (int y = y1; y < y2; y++)
            {
                int rowBase = (y * width + x1) * 3;
                FillRgbTriplets(pixels.AsSpan(rowBase, rectWidth * 3), roughness, metallic, emissive);
            }

            filledRects++;
        }

        return new Result(pixelBuffersByAtlasTexId, sizesByAtlasTexId, filledRects);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillRgbTriplets(float[] destination, float r, float g, float b)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        FillRgbTriplets(destination.AsSpan(), r, g, b);
    }

    /// <summary>
    /// Fills <paramref name="destination"/> (a multiple-of-3 length span) with repeating RGB triplets: r,g,b,r,g,b,...
    /// Uses Vector256 (AVX) or Vector128 (SSE) when supported; falls back to scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillRgbTriplets(Span<float> destination, float r, float g, float b)
    {
        if (destination.Length == 0)
        {
            return;
        }

        // These spans are always produced by code paths that multiply pixel counts by 3.
        // Keep a fast debug-time check but avoid throwing in release builds.
        System.Diagnostics.Debug.Assert(destination.Length % 3 == 0);

        if (Avx.IsSupported)
        {
            FillRgbTripletsVector256Avx(destination, r, g, b);
            return;
        }

        if (Sse.IsSupported)
        {
            FillRgbTripletsVector128Sse(destination, r, g, b);
            return;
        }

        FillRgbTripletsScalar(destination, r, g, b);
    }

    internal static void FillRgbTripletsScalar(Span<float> destination, float r, float g, float b)
    {
        if (destination.Length == 0)
        {
            return;
        }

        System.Diagnostics.Debug.Assert(destination.Length % 3 == 0);

        for (int i = 0; i < destination.Length; i += 3)
        {
            destination[i + 0] = r;
            destination[i + 1] = g;
            destination[i + 2] = b;
        }
    }

    internal static void FillRgbTripletsVector128Sse(Span<float> destination, float r, float g, float b)
    {
        if (!Sse.IsSupported)
        {
            throw new PlatformNotSupportedException("SSE is not supported on this platform.");
        }

        if (destination.Length == 0)
        {
            return;
        }

        System.Diagnostics.Debug.Assert(destination.Length % 3 == 0);

        ref float dstRef = ref MemoryMarshal.GetReference(destination);
        int length = destination.Length;

        // 4 floats per vector. Write 3 vectors (12 floats) per iteration to preserve the RGB triplet phase.
        Vector128<float> v0 = Vector128.Create(r, g, b, r);
        Vector128<float> v1 = Vector128.Create(g, b, r, g);
        Vector128<float> v2 = Vector128.Create(b, r, g, b);

        int i = 0;
        for (; i <= length - 12; i += 12)
        {
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 0)), v0);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 4)), v1);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 8)), v2);
        }

        for (; i < length; i += 3)
        {
            Unsafe.Add(ref dstRef, i + 0) = r;
            Unsafe.Add(ref dstRef, i + 1) = g;
            Unsafe.Add(ref dstRef, i + 2) = b;
        }
    }

    internal static void FillRgbTripletsVector256Avx(Span<float> destination, float r, float g, float b)
    {
        if (!Avx.IsSupported)
        {
            throw new PlatformNotSupportedException("AVX is not supported on this platform.");
        }

        if (destination.Length == 0)
        {
            return;
        }

        System.Diagnostics.Debug.Assert(destination.Length % 3 == 0);

        ref float dstRef = ref MemoryMarshal.GetReference(destination);
        int length = destination.Length;

        // 8 floats per vector. Write 3 vectors (24 floats) per iteration to preserve the RGB triplet phase.
        Vector256<float> v0 = Vector256.Create(r, g, b, r, g, b, r, g);
        Vector256<float> v1 = Vector256.Create(b, r, g, b, r, g, b, r);
        Vector256<float> v2 = Vector256.Create(g, b, r, g, b, r, g, b);

        int i = 0;
        for (; i <= length - 24; i += 24)
        {
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 0)), v0);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 8)), v1);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i + 16)), v2);
        }

        // Tail: scalar is enough here (still correct, and avoids depending on SSE from this method).
        for (; i < length; i += 3)
        {
            Unsafe.Add(ref dstRef, i + 0) = r;
            Unsafe.Add(ref dstRef, i + 1) = g;
            Unsafe.Add(ref dstRef, i + 2) = b;
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }
}
