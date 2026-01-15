using System;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class PbrMaterialParamsPixelBuilderNoiseTests
{
    private static uint Fnv1a32(ReadOnlySpan<int> values)
    {
        const uint offset = 2_166_136_261u;
        const uint prime = 16_777_619u;

        uint hash = offset;
        foreach (int v in values)
        {
            unchecked
            {
                hash ^= (uint)v;
                hash *= prime;
            }
        }
        return hash;
    }

    private static uint HashFloatBits(ReadOnlySpan<float> values)
    {
        Span<int> bits = values.Length <= 512 ? stackalloc int[values.Length] : new int[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            bits[i] = BitConverter.SingleToInt32Bits(values[i]);
        }
        return Fnv1a32(bits);
    }

    [Fact]
    public void ApplyNoiseRow_DeterministicAcrossCalls()
    {
        const int pixelCount = 32;
        Span<float> a = stackalloc float[pixelCount * 3];
        Span<float> b = stackalloc float[pixelCount * 3];

        PbrMaterialParamsPixelBuilder.FillRgbTripletsScalar(a, 0.5f, 0.5f, 0.5f);
        PbrMaterialParamsPixelBuilder.FillRgbTripletsScalar(b, 0.5f, 0.5f, 0.5f);

        PbrMaterialParamsPixelBuilder.ApplyNoiseRowScalar(a, pixelCount, seed: 123u, localY: 7u, baseR: 0.5f, baseG: 0.5f, baseB: 0.5f, ampR: 0.2f, ampG: 0.1f, ampB: 0.05f);
        PbrMaterialParamsPixelBuilder.ApplyNoiseRowScalar(b, pixelCount, seed: 123u, localY: 7u, baseR: 0.5f, baseG: 0.5f, baseB: 0.5f, ampR: 0.2f, ampG: 0.1f, ampB: 0.05f);

        Assert.True(a.SequenceEqual(b));
    }

    [Fact]
    public void ApplyNoiseRow_SnapshotChecksum_IsStable()
    {
        const int pixelCount = 17;
        Span<float> row = stackalloc float[pixelCount * 3];
        PbrMaterialParamsPixelBuilder.FillRgbTripletsScalar(row, 0.5f, 0.25f, 0.75f);

        PbrMaterialParamsPixelBuilder.ApplyNoiseRowScalar(
            row,
            pixelCount,
            seed: 123u,
            localY: 7u,
            baseR: 0.5f,
            baseG: 0.25f,
            baseB: 0.75f,
            ampR: 0.2f,
            ampG: 0.1f,
            ampB: 0.05f);

        uint actual = HashFloatBits(row);

        // Snapshot: if this changes, noise output changed.
        const uint expected = 2_087_666_905u;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ApplyNoiseRow_Vector128_MatchesScalar()
    {
        Assert.SkipWhen(!(System.Runtime.Intrinsics.X86.Sse2.IsSupported && System.Runtime.Intrinsics.X86.Sse41.IsSupported), "SSE2+SSE4.1 not supported on this platform");

        const int pixelCount = 33;
        Span<float> scalar = stackalloc float[pixelCount * 3];
        Span<float> simd = stackalloc float[pixelCount * 3];

        PbrMaterialParamsPixelBuilder.FillRgbTripletsScalar(scalar, 0.6f, 0.2f, 0.1f);
        PbrMaterialParamsPixelBuilder.FillRgbTripletsScalar(simd, 0.6f, 0.2f, 0.1f);

        PbrMaterialParamsPixelBuilder.ApplyNoiseRowScalar(scalar, pixelCount, seed: 456u, localY: 3u, baseR: 0.6f, baseG: 0.2f, baseB: 0.1f, ampR: 0.3f, ampG: 0.2f, ampB: 0.1f);
        PbrMaterialParamsPixelBuilder.ApplyNoiseRowVector128(simd, pixelCount, seed: 456u, localY: 3u, baseR: 0.6f, baseG: 0.2f, baseB: 0.1f, ampR: 0.3f, ampG: 0.2f, ampB: 0.1f);

        Assert.True(scalar.SequenceEqual(simd));
    }

    [Fact]
    public void ApplyNoiseRow_Vector256_MatchesScalar()
    {
        Assert.SkipWhen(!System.Runtime.Intrinsics.X86.Avx2.IsSupported, "AVX2 not supported on this platform");

        const int pixelCount = 64;
        Span<float> scalar = stackalloc float[pixelCount * 3];
        Span<float> simd = stackalloc float[pixelCount * 3];

        PbrMaterialParamsPixelBuilder.FillRgbTripletsScalar(scalar, 0.9f, 0.0f, 0.0f);
        PbrMaterialParamsPixelBuilder.FillRgbTripletsScalar(simd, 0.9f, 0.0f, 0.0f);

        PbrMaterialParamsPixelBuilder.ApplyNoiseRowScalar(scalar, pixelCount, seed: 789u, localY: 0u, baseR: 0.9f, baseG: 0.0f, baseB: 0.0f, ampR: 0.15f, ampG: 0.05f, ampB: 0.2f);
        PbrMaterialParamsPixelBuilder.ApplyNoiseRowVector256(simd, pixelCount, seed: 789u, localY: 0u, baseR: 0.9f, baseG: 0.0f, baseB: 0.0f, ampR: 0.15f, ampG: 0.05f, ampB: 0.2f);

        Assert.True(scalar.SequenceEqual(simd));
    }

    [Fact]
    public void BuildRgb16fPixelBuffers_AppliesNoiseWithinRect_AndIsDeterministic()
    {
        var atlasPages = new[] { (atlasTextureId: 1, width: 16, height: 16) };

        var tex = new AssetLocation("GAME", "block/test");

        var pos = new TextureAtlasPosition
        {
            atlasTextureId = 1,
            x1 = 0f,
            y1 = 0f,
            x2 = 0.5f,
            y2 = 0.5f
        };

        var texturePositions = new Dictionary<AssetLocation, TextureAtlasPosition>
        {
            [tex] = pos
        };

        var def = new PbrMaterialDefinition
        {
            Roughness = 0.5f,
            Metallic = 0.2f,
            Emissive = 0.1f,
            Noise = new PbrMaterialNoise
            {
                Roughness = 0.25f,
                Metallic = 0.15f,
                Emissive = 0.4f
            }
        };

        var materialsByTexture = new Dictionary<AssetLocation, PbrMaterialDefinition>
        {
            [tex] = def
        };

        var r1 = PbrMaterialParamsPixelBuilder.BuildRgb16fPixelBuffers(atlasPages, texturePositions, materialsByTexture);
        var r2 = PbrMaterialParamsPixelBuilder.BuildRgb16fPixelBuffers(atlasPages, texturePositions, materialsByTexture);

        float[] pixels1 = r1.PixelBuffersByAtlasTexId[1];
        float[] pixels2 = r2.PixelBuffersByAtlasTexId[1];

        Assert.Equal(pixels1.Length, pixels2.Length);
        Assert.True(pixels1.AsSpan().SequenceEqual(pixels2));

        // Within the rect (0..7,0..7), at least two pixels should differ due to noise.
        // Pick two different local coords.
        int width = 16;
        int p00 = (0 * width + 0) * 3;
        int p77 = (7 * width + 7) * 3;

        bool different =
            pixels1[p00 + 0] != pixels1[p77 + 0]
            || pixels1[p00 + 1] != pixels1[p77 + 1]
            || pixels1[p00 + 2] != pixels1[p77 + 2];

        Assert.True(different);
    }

    [Fact]
    public void BuildRgb16fPixelBuffers_SnapshotChecksum_IsStable()
    {
        var atlasPages = new[] { (atlasTextureId: 1, width: 16, height: 16) };

        var tex = new AssetLocation("game", "block/test");

        var pos = new TextureAtlasPosition
        {
            atlasTextureId = 1,
            x1 = 0f,
            y1 = 0f,
            x2 = 0.5f,
            y2 = 0.5f
        };

        var texturePositions = new Dictionary<AssetLocation, TextureAtlasPosition>
        {
            [tex] = pos
        };

        var def = new PbrMaterialDefinition
        {
            Roughness = 0.5f,
            Metallic = 0.2f,
            Emissive = 0.1f,
            Noise = new PbrMaterialNoise
            {
                Roughness = 0.25f,
                Metallic = 0.15f,
                Emissive = 0.4f
            }
        };

        var materialsByTexture = new Dictionary<AssetLocation, PbrMaterialDefinition>
        {
            [tex] = def
        };

        var r = PbrMaterialParamsPixelBuilder.BuildRgb16fPixelBuffers(atlasPages, texturePositions, materialsByTexture);
        float[] pixels = r.PixelBuffersByAtlasTexId[1];

        uint actual = HashFloatBits(pixels);

        // Snapshot: if this changes, bake output changed.
        const uint expected = 3_572_877_435u;
        Assert.Equal(expected, actual);
    }
}
