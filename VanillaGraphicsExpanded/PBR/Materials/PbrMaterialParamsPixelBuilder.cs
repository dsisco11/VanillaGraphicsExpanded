using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Noise;

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

    private const uint RoughnessSalt = 0xA2C79E15u;
    private const uint MetallicSalt = 0x3C6EF372u;
    private const uint EmissiveSalt = 0x9E3779B9u;

    private const float InvFloatMax = 1f / Squirrel3Noise.FloatMax;

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
            SimdSpanMath.FillInterleaved3(pixels, DefaultRoughness, DefaultMetallic, DefaultEmissive);

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

            float roughnessNoise = Clamp01(definition.Noise.Roughness);
            float metallicNoise = Clamp01(definition.Noise.Metallic);
            float emissiveNoise = Clamp01(definition.Noise.Emissive);

            uint seed = StableHash32.HashAssetLocation(texture);

            for (int y = y1; y < y2; y++)
            {
                int rowBase = (y * width + x1) * 3;
                Span<float> rowSpan = pixels.AsSpan(rowBase, rectWidth * 3);

                SimdSpanMath.FillInterleaved3(rowSpan, roughness, metallic, emissive);

                if (roughnessNoise != 0f || metallicNoise != 0f || emissiveNoise != 0f)
                {
                    uint localY = (uint)(y - y1);
                    ApplyNoiseRow(
                        rowSpan,
                        rectWidth,
                        seed,
                        localY,
                        roughness,
                        metallic,
                        emissive,
                        roughnessNoise,
                        metallicNoise,
                        emissiveNoise);
                }
            }

            filledRects++;
        }

        return new Result(pixelBuffersByAtlasTexId, sizesByAtlasTexId, filledRects);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyNoiseRow(
        Span<float> rowRgb,
        int pixelCount,
        uint seed,
        uint localY,
        float baseR,
        float baseG,
        float baseB,
        float ampR,
        float ampG,
        float ampB)
    {
        if (pixelCount <= 0)
        {
            return;
        }

        if (Avx2.IsSupported)
        {
            ApplyNoiseRowVector256(rowRgb, pixelCount, seed, localY, baseR, baseG, baseB, ampR, ampG, ampB);
            return;
        }

        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            ApplyNoiseRowVector128(rowRgb, pixelCount, seed, localY, baseR, baseG, baseB, ampR, ampG, ampB);
            return;
        }

        ApplyNoiseRowScalar(rowRgb, pixelCount, seed, localY, baseR, baseG, baseB, ampR, ampG, ampB);
    }

    internal static void ApplyNoiseRowScalar(
        Span<float> rowRgb,
        int pixelCount,
        uint seed,
        uint localY,
        float baseR,
        float baseG,
        float baseB,
        float ampR,
        float ampG,
        float ampB)
    {
        uint seedR = seed ^ RoughnessSalt;
        uint seedG = seed ^ MetallicSalt;
        uint seedB = seed ^ EmissiveSalt;

        for (int x = 0; x < pixelCount; x++)
        {
            uint ux = (uint)x;

            float nR = NoiseSigned(seedR, ux, localY);
            float nG = NoiseSigned(seedG, ux, localY);
            float nB = NoiseSigned(seedB, ux, localY);

            int i = x * 3;
            rowRgb[i + 0] = Clamp01(baseR + (nR * ampR));
            rowRgb[i + 1] = Clamp01(baseG + (nG * ampG));
            rowRgb[i + 2] = Clamp01(baseB + (nB * ampB));
        }
    }

    internal static void ApplyNoiseRowVector128(
        Span<float> rowRgb,
        int pixelCount,
        uint seed,
        uint localY,
        float baseR,
        float baseG,
        float baseB,
        float ampR,
        float ampG,
        float ampB)
    {
        if (!(Sse2.IsSupported && Sse41.IsSupported))
        {
            ApplyNoiseRowScalar(rowRgb, pixelCount, seed, localY, baseR, baseG, baseB, ampR, ampG, ampB);
            return;
        }

        uint seedR = seed ^ RoughnessSalt;
        uint seedG = seed ^ MetallicSalt;
        uint seedB = seed ^ EmissiveSalt;

        Vector128<uint> v1R = Vector128.Create(seedR);
        Vector128<uint> v1G = Vector128.Create(seedG);
        Vector128<uint> v1B = Vector128.Create(seedB);
        Vector128<uint> v3 = Vector128.Create(localY);

        Vector128<uint> offsets0 = Vector128.Create(0u, 1u, 2u, 3u);

        int x = 0;
        for (; x <= pixelCount - 4; x += 4)
        {
            Vector128<uint> v2 = Sse2.Add(offsets0, Vector128.Create((uint)x));

            Vector128<uint> uR = Squirrel3Noise.HashU_Vector128_U32(v1R, v2, v3);
            Vector128<uint> uG = Squirrel3Noise.HashU_Vector128_U32(v1G, v2, v3);
            Vector128<uint> uB = Squirrel3Noise.HashU_Vector128_U32(v1B, v2, v3);

            for (int lane = 0; lane < 4; lane++)
            {
                float nR = NoiseSignedFromU32(uR.GetElement(lane));
                float nG = NoiseSignedFromU32(uG.GetElement(lane));
                float nB = NoiseSignedFromU32(uB.GetElement(lane));

                int i = (x + lane) * 3;
                rowRgb[i + 0] = Clamp01(baseR + (nR * ampR));
                rowRgb[i + 1] = Clamp01(baseG + (nG * ampG));
                rowRgb[i + 2] = Clamp01(baseB + (nB * ampB));
            }
        }

        for (; x < pixelCount; x++)
        {
            uint ux = (uint)x;

            float nR = NoiseSigned(seedR, ux, localY);
            float nG = NoiseSigned(seedG, ux, localY);
            float nB = NoiseSigned(seedB, ux, localY);

            int i = x * 3;
            rowRgb[i + 0] = Clamp01(baseR + (nR * ampR));
            rowRgb[i + 1] = Clamp01(baseG + (nG * ampG));
            rowRgb[i + 2] = Clamp01(baseB + (nB * ampB));
        }
    }

    internal static void ApplyNoiseRowVector256(
        Span<float> rowRgb,
        int pixelCount,
        uint seed,
        uint localY,
        float baseR,
        float baseG,
        float baseB,
        float ampR,
        float ampG,
        float ampB)
    {
        if (!Avx2.IsSupported)
        {
            ApplyNoiseRowScalar(rowRgb, pixelCount, seed, localY, baseR, baseG, baseB, ampR, ampG, ampB);
            return;
        }

        uint seedR = seed ^ RoughnessSalt;
        uint seedG = seed ^ MetallicSalt;
        uint seedB = seed ^ EmissiveSalt;

        Vector256<uint> v1R = Vector256.Create(seedR);
        Vector256<uint> v1G = Vector256.Create(seedG);
        Vector256<uint> v1B = Vector256.Create(seedB);
        Vector256<uint> v3 = Vector256.Create(localY);

        Vector256<uint> offsets0 = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);

        int x = 0;
        for (; x <= pixelCount - 8; x += 8)
        {
            Vector256<uint> v2 = Avx2.Add(offsets0, Vector256.Create((uint)x));

            Vector256<uint> uR = Squirrel3Noise.HashU_Vector256_U32(v1R, v2, v3);
            Vector256<uint> uG = Squirrel3Noise.HashU_Vector256_U32(v1G, v2, v3);
            Vector256<uint> uB = Squirrel3Noise.HashU_Vector256_U32(v1B, v2, v3);

            for (int lane = 0; lane < 8; lane++)
            {
                float nR = NoiseSignedFromU32(uR.GetElement(lane));
                float nG = NoiseSignedFromU32(uG.GetElement(lane));
                float nB = NoiseSignedFromU32(uB.GetElement(lane));

                int i = (x + lane) * 3;
                rowRgb[i + 0] = Clamp01(baseR + (nR * ampR));
                rowRgb[i + 1] = Clamp01(baseG + (nG * ampG));
                rowRgb[i + 2] = Clamp01(baseB + (nB * ampB));
            }
        }

        for (; x < pixelCount; x++)
        {
            uint ux = (uint)x;

            float nR = NoiseSigned(seedR, ux, localY);
            float nG = NoiseSigned(seedG, ux, localY);
            float nB = NoiseSigned(seedB, ux, localY);

            int i = x * 3;
            rowRgb[i + 0] = Clamp01(baseR + (nR * ampR));
            rowRgb[i + 1] = Clamp01(baseG + (nG * ampG));
            rowRgb[i + 2] = Clamp01(baseB + (nB * ampB));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float NoiseSigned(uint saltedSeed, uint localX, uint localY)
    {
        uint u = Squirrel3Noise.HashU(saltedSeed, localX, localY);
        return NoiseSignedFromU32(u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float NoiseSignedFromU32(uint u)
    {
        // Match the SIMD float conversion strategy used by Squirrel3Noise.Noise01 SIMD paths.
        // float(u) = float((u >> 1) | (u & 1)) * 2
        uint adjusted = (u >> 1) | (u & 1u);
        float uFloat = adjusted * 2f;

        float t = uFloat * InvFloatMax;
        return (t * 2f) - 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void FillRgbTriplets(float[] destination, float r, float g, float b)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        SimdSpanMath.FillInterleaved3(destination, r, g, b);
    }

    /// <summary>
    /// Fills <paramref name="destination"/> (a multiple-of-3 length span) with repeating RGB triplets: r,g,b,r,g,b,...
    /// Uses Vector256 (AVX) or Vector128 (SSE) when supported; falls back to scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void FillRgbTriplets(Span<float> destination, float r, float g, float b)
    {
        SimdSpanMath.FillInterleaved3(destination, r, g, b);
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
