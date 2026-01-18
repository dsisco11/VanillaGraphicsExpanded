using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Numerics.Tensors;

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

    private const int NoisePlanarStackallocThreshold = 256;

    private static readonly bool UsePlanarNoiseSnt = true;

    private const int MaterialBakeBlueNoiseSize = 64;
    private const uint MaterialBakeBlueNoiseSeed = 0xC0FFEEu;
    private const int MaterialBakeBlueNoiseMaxIterations = 4096;

    private static readonly BlueNoiseCache MaterialBakeBlueNoiseCache = new();
    private static readonly BlueNoiseConfig MaterialBakeBlueNoiseConfig = new(
        Width: MaterialBakeBlueNoiseSize,
        Height: MaterialBakeBlueNoiseSize,
        Slices: 1,
        Tileable: true,
        Seed: MaterialBakeBlueNoiseSeed,
        Algorithm: BlueNoiseAlgorithm.VoidAndCluster,
        OutputKind: BlueNoiseOutputKind.RankU16,
        Sigma: 1.0f,
        InitialFillRatio: 0.12f,
        MaxIterations: MaterialBakeBlueNoiseMaxIterations,
        StagnationLimit: 0);

    private static float[]? materialBakeBlueNoiseSignedTile;

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

            if (!AtlasRectResolver.TryResolvePixelRect(texPos, width, height, out AtlasRect rect))
            {
                continue;
            }

            int x1 = rect.X;
            int y1 = rect.Y;
            int x2 = rect.Right;
            int y2 = rect.Bottom;
            int rectWidth = rect.Width;
            int rectHeight = rect.Height;

            float roughness = Clamp01(definition.Roughness);
            float metallic = Clamp01(definition.Metallic);
            float emissive = Clamp01(definition.Emissive);

            float roughnessNoise = Clamp01(definition.Noise.Roughness);
            float metallicNoise = Clamp01(definition.Noise.Metallic);
            float emissiveNoise = Clamp01(definition.Noise.Emissive);

            PbrOverrideScale scale = definition.Scale;
            bool scaleIsIdentity = scale.Roughness == 1f && scale.Metallic == 1f && scale.Emissive == 1f;

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

            if (!scaleIsIdentity)
            {
                int rectStart = (y1 * width + x1) * 3;
                SimdSpanMath.MultiplyClamp01Interleaved3InPlace2D(
                    destination3: pixels.AsSpan(rectStart),
                    rectWidthPixels: rectWidth,
                    rectHeightPixels: rectHeight,
                    rowStridePixels: width,
                    mul0: scale.Roughness,
                    mul1: scale.Metallic,
                    mul2: scale.Emissive);
            }

            filledRects++;
        }

        return new Result(pixelBuffersByAtlasTexId, sizesByAtlasTexId, filledRects);
    }

    public static float[] BuildRgb16fTile(
        AssetLocation texture,
        PbrMaterialDefinition definition,
        int rectWidth,
        int rectHeight,
        CancellationToken cancellationToken)
    {
        if (texture is null) throw new ArgumentNullException(nameof(texture));
        if (rectWidth <= 0) throw new ArgumentOutOfRangeException(nameof(rectWidth));
        if (rectHeight <= 0) throw new ArgumentOutOfRangeException(nameof(rectHeight));

        cancellationToken.ThrowIfCancellationRequested();

        float roughness = Clamp01(definition.Roughness);
        float metallic = Clamp01(definition.Metallic);
        float emissive = Clamp01(definition.Emissive);

        float roughnessNoise = Clamp01(definition.Noise.Roughness);
        float metallicNoise = Clamp01(definition.Noise.Metallic);
        float emissiveNoise = Clamp01(definition.Noise.Emissive);

        PbrOverrideScale scale = definition.Scale;
        bool scaleIsIdentity = scale.Roughness == 1f && scale.Metallic == 1f && scale.Emissive == 1f;

        uint seed = StableHash32.HashAssetLocation(texture);

        float[] rgb = new float[checked(rectWidth * rectHeight * 3)];

        for (int y = 0; y < rectHeight; y++)
        {
            if ((y & 15) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int rowBase = (y * rectWidth) * 3;
            Span<float> rowSpan = rgb.AsSpan(rowBase, rectWidth * 3);

            SimdSpanMath.FillInterleaved3(rowSpan, roughness, metallic, emissive);

            if (roughnessNoise != 0f || metallicNoise != 0f || emissiveNoise != 0f)
            {
                ApplyNoiseRow(
                    rowSpan,
                    rectWidth,
                    seed,
                    localY: (uint)y,
                    baseR: roughness,
                    baseG: metallic,
                    baseB: emissive,
                    ampR: roughnessNoise,
                    ampG: metallicNoise,
                    ampB: emissiveNoise);
            }
        }

        if (!scaleIsIdentity)
        {
            SimdSpanMath.MultiplyClamp01Interleaved3InPlace2D(
                destination3: rgb,
                rectWidthPixels: rectWidth,
                rectHeightPixels: rectHeight,
                rowStridePixels: rectWidth,
                mul0: scale.Roughness,
                mul1: scale.Metallic,
                mul2: scale.Emissive);
        }

        return rgb;
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

        if (UsePlanarNoiseSnt)
        {
            // Planar noise buffers let us use SNT/TensorPrimitives for the hot math (mul/add/clamp)
            // while keeping the custom hashing unchanged.
            ApplyNoiseRowPlanarSnt(rowRgb, pixelCount, seed, localY, baseR, baseG, baseB, ampR, ampG, ampB);
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

    private static void ApplyNoiseRowPlanarSnt(
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

        if (pixelCount <= NoisePlanarStackallocThreshold)
        {
            Span<float> noise = stackalloc float[pixelCount * 3];
            Span<float> noiseR = noise.Slice(0, pixelCount);
            Span<float> noiseG = noise.Slice(pixelCount, pixelCount);
            Span<float> noiseB = noise.Slice(pixelCount * 2, pixelCount);

            FillNoiseAndApply(rowRgb, pixelCount, seedR, seedG, seedB, localY, baseR, baseG, baseB, ampR, ampG, ampB, noiseR, noiseG, noiseB);
            return;
        }

        float[] rented = ArrayPool<float>.Shared.Rent(pixelCount * 3);
        try
        {
            Span<float> noise = rented.AsSpan(0, pixelCount * 3);
            Span<float> noiseR = noise.Slice(0, pixelCount);
            Span<float> noiseG = noise.Slice(pixelCount, pixelCount);
            Span<float> noiseB = noise.Slice(pixelCount * 2, pixelCount);

            FillNoiseAndApply(rowRgb, pixelCount, seedR, seedG, seedB, localY, baseR, baseG, baseB, ampR, ampG, ampB, noiseR, noiseG, noiseB);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillNoiseAndApply(
        Span<float> rowRgb,
        int pixelCount,
        uint seedR,
        uint seedG,
        uint seedB,
        uint localY,
        float baseR,
        float baseG,
        float baseB,
        float ampR,
        float ampG,
        float ampB,
        Span<float> noiseR,
        Span<float> noiseG,
        Span<float> noiseB)
    {
        // Fill planar noise buffers via wrapped block copies from a cached tileable blue-noise map.
        // This avoids per-pixel hashing and keeps the hot path in bulk operations.
        FillBlueNoiseRowSigned(noiseR, seedR, localY);
        FillBlueNoiseRowSigned(noiseG, seedG, localY);
        FillBlueNoiseRowSigned(noiseB, seedB, localY);

        // base + (noise * amp), clamped to [0,1]
        SimdSpanMath.ScaleInPlace(noiseR, ampR);
        SimdSpanMath.AddInPlace(noiseR, baseR);
        System.Numerics.Tensors.TensorPrimitives.Clamp(noiseR, 0f, 1f, noiseR);

        SimdSpanMath.ScaleInPlace(noiseG, ampG);
        SimdSpanMath.AddInPlace(noiseG, baseG);
        System.Numerics.Tensors.TensorPrimitives.Clamp(noiseG, 0f, 1f, noiseG);

        SimdSpanMath.ScaleInPlace(noiseB, ampB);
        SimdSpanMath.AddInPlace(noiseB, baseB);
        System.Numerics.Tensors.TensorPrimitives.Clamp(noiseB, 0f, 1f, noiseB);

        for (int x = 0; x < pixelCount; x++)
        {
            int i = x * 3;
            rowRgb[i + 0] = noiseR[x];
            rowRgb[i + 1] = noiseG[x];
            rowRgb[i + 2] = noiseB[x];
        }
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
        // Blue-noise path uses TensorPrimitives (SIMD) for the hot math.
        // Keep this method SIMD-accelerated by routing through the planar SNT implementation.
        ApplyNoiseRowPlanarSnt(rowRgb, pixelCount, seed, localY, baseR, baseG, baseB, ampR, ampG, ampB);
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
        // Blue-noise path uses TensorPrimitives (SIMD) for the hot math.
        // Keep this method SIMD-accelerated by routing through the planar SNT implementation.
        ApplyNoiseRowPlanarSnt(rowRgb, pixelCount, seed, localY, baseR, baseG, baseB, ampR, ampG, ampB);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float NoiseSigned(uint saltedSeed, uint localX, uint localY)
    {
        return BlueNoiseSigned(saltedSeed, localX, localY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float[] GetMaterialBakeBlueNoiseSignedTile()
    {
        float[]? tile = Volatile.Read(ref materialBakeBlueNoiseSignedTile);
        if (tile is not null)
        {
            return tile;
        }

        return LazyInitializer.EnsureInitialized(ref materialBakeBlueNoiseSignedTile, static () =>
        {
            BlueNoiseRankMap map = MaterialBakeBlueNoiseCache.GetOrCreateRankMap(
                MaterialBakeBlueNoiseConfig,
                static c => VoidAndClusterGenerator.GenerateRankMap(c));

            int n = map.Length;
            float inv = n > 1 ? 1f / (n - 1) : 0f;

            float[] signed = new float[n];
            ReadOnlySpan<ushort> ranks = map.RanksSpan;
            for (int i = 0; i < n; i++)
            {
                float t = ranks[i] * inv;
                signed[i] = (t * 2f) - 1f;
            }

            return signed;
        });
    }

    private static void FillBlueNoiseRowSigned(Span<float> destination, uint saltedSeed, uint localY)
    {
        if (destination.IsEmpty)
        {
            return;
        }

        // Cache tile is fixed-size and tileable.
        float[] signedTile = GetMaterialBakeBlueNoiseSignedTile();
        int w = MaterialBakeBlueNoiseConfig.Width;
        int h = MaterialBakeBlueNoiseConfig.Height;

        // Derive a deterministic per-saltedSeed tile offset to avoid repeating the same phase across materials/channels.
        uint m0 = Mix32(saltedSeed);
        uint ox = (uint)(m0 % (uint)w);
        uint oy = (uint)(Mix32(m0 ^ 0x9E3779B9u) % (uint)h);

        int y = (int)((localY + oy) % (uint)h);
        int rowBase = y * w;
        int x = (int)ox;

        int remaining = destination.Length;
        int dst = 0;
        while (remaining > 0)
        {
            int run = Math.Min(remaining, w - x);
            signedTile.AsSpan(rowBase + x, run).CopyTo(destination.Slice(dst, run));

            dst += run;
            remaining -= run;
            x = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float BlueNoiseSigned(uint saltedSeed, uint localX, uint localY)
    {
        BlueNoiseRankMap map = MaterialBakeBlueNoiseCache.GetOrCreateRankMap(
            MaterialBakeBlueNoiseConfig,
            static c => VoidAndClusterGenerator.GenerateRankMap(c));

        int w = map.Width;
        int h = map.Height;
        ReadOnlySpan<ushort> ranks = map.RanksSpan;

        // Derive a deterministic per-saltedSeed tile offset to avoid repeating the same phase across materials/channels.
        uint m0 = Mix32(saltedSeed);
        uint ox = (uint)(m0 % (uint)w);
        uint oy = (uint)(Mix32(m0 ^ 0x9E3779B9u) % (uint)h);

        int x = (int)((localX + ox) % (uint)w);
        int y = (int)((localY + oy) % (uint)h);

        int idx = (y * w) + x;
        ushort rank = ranks[idx];

        int n = map.Length;
        float t = n > 1 ? rank * (1f / (n - 1)) : 0f;
        return (t * 2f) - 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Mix32(uint x)
    {
        // Small, fast avalanche mix for deriving tile offsets.
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
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

    internal static void FillRgbTripletsScalar(Span<float> destination, float r, float g, float b)
    {
        SimdSpanMath.FillInterleaved3Scalar(destination, r, g, b);
    }

    internal static void FillRgbTripletsVector128Sse(Span<float> destination, float r, float g, float b)
    {
        SimdSpanMath.FillInterleaved3Vector128Sse(destination, r, g, b);
    }

    internal static void FillRgbTripletsVector256Avx(Span<float> destination, float r, float g, float b)
    {
        SimdSpanMath.FillInterleaved3Vector256Avx(destination, r, g, b);
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
