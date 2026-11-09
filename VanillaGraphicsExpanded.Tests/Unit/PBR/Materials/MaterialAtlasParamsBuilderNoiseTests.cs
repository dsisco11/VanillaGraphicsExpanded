using System;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies deterministic material-atlas noise, SIMD parity, and normalized output constraints.</summary>
public sealed class MaterialAtlasParamsBuilderNoiseTests
{
    #region Public API
    /// <summary>Equal input rows produce identical seeded noise across repeated scalar calls.</summary>
    [Fact]
    public void ApplyNoiseRow_DeterministicAcrossCalls()
    {
        // Arrange: initialize matching RGB rows with identical seed and noise parameters.
        const int pixelCount = 32;
        Span<float> a = stackalloc float[pixelCount * 3];
        Span<float> b = stackalloc float[pixelCount * 3];

        MaterialAtlasParamsBuilder.FillRgbTripletsScalar(a, 0.5f, 0.5f, 0.5f);
        MaterialAtlasParamsBuilder.FillRgbTripletsScalar(b, 0.5f, 0.5f, 0.5f);

        // Act: apply the same deterministic scalar transform to each row.
        MaterialAtlasParamsBuilder.ApplyNoiseRowScalar(a, pixelCount, seed: 123u, localY: 7u, baseR: 0.5f, baseG: 0.5f, baseB: 0.5f, ampR: 0.2f, ampG: 0.1f, ampB: 0.05f);
        MaterialAtlasParamsBuilder.ApplyNoiseRowScalar(b, pixelCount, seed: 123u, localY: 7u, baseR: 0.5f, baseG: 0.5f, baseB: 0.5f, ampR: 0.2f, ampG: 0.1f, ampB: 0.05f);

        // Assert: repeated transforms produce the same pixel sequence.
        Assert.True(a.SequenceEqual(b));
    }

    /// <summary>The Vector128 implementation matches scalar output on supported x86 SIMD hardware.</summary>
    [Fact]
    public void ApplyNoiseRow_Vector128_MatchesScalar()
    {
        // Arrange: require the instruction set used by the Vector128 implementation and initialize equal rows.
        Assert.SkipWhen(!(System.Runtime.Intrinsics.X86.Sse2.IsSupported && System.Runtime.Intrinsics.X86.Sse41.IsSupported), "SSE2+SSE4.1 not supported on this platform");

        const int pixelCount = 33;
        Span<float> scalar = stackalloc float[pixelCount * 3];
        Span<float> simd = stackalloc float[pixelCount * 3];

        MaterialAtlasParamsBuilder.FillRgbTripletsScalar(scalar, 0.6f, 0.2f, 0.1f);
        MaterialAtlasParamsBuilder.FillRgbTripletsScalar(simd, 0.6f, 0.2f, 0.1f);

        // Act: run scalar and Vector128 transforms with identical parameters.
        MaterialAtlasParamsBuilder.ApplyNoiseRowScalar(scalar, pixelCount, seed: 456u, localY: 3u, baseR: 0.6f, baseG: 0.2f, baseB: 0.1f, ampR: 0.3f, ampG: 0.2f, ampB: 0.1f);
        MaterialAtlasParamsBuilder.ApplyNoiseRowVector128(simd, pixelCount, seed: 456u, localY: 3u, baseR: 0.6f, baseG: 0.2f, baseB: 0.1f, ampR: 0.3f, ampG: 0.2f, ampB: 0.1f);

        // Assert: SIMD output matches the scalar reference exactly.
        Assert.True(scalar.SequenceEqual(simd));
    }

    /// <summary>The Vector256 implementation matches scalar output on supported AVX2 hardware.</summary>
    [Fact]
    public void ApplyNoiseRow_Vector256_MatchesScalar()
    {
        // Arrange: require AVX2 and initialize identical scalar and SIMD rows.
        Assert.SkipWhen(!System.Runtime.Intrinsics.X86.Avx2.IsSupported, "AVX2 not supported on this platform");

        const int pixelCount = 64;
        Span<float> scalar = stackalloc float[pixelCount * 3];
        Span<float> simd = stackalloc float[pixelCount * 3];

        MaterialAtlasParamsBuilder.FillRgbTripletsScalar(scalar, 0.9f, 0.0f, 0.0f);
        MaterialAtlasParamsBuilder.FillRgbTripletsScalar(simd, 0.9f, 0.0f, 0.0f);

        // Act: run both implementations for the same seeded row.
        MaterialAtlasParamsBuilder.ApplyNoiseRowScalar(scalar, pixelCount, seed: 789u, localY: 0u, baseR: 0.9f, baseG: 0.0f, baseB: 0.0f, ampR: 0.15f, ampG: 0.05f, ampB: 0.2f);
        MaterialAtlasParamsBuilder.ApplyNoiseRowVector256(simd, pixelCount, seed: 789u, localY: 0u, baseR: 0.9f, baseG: 0.0f, baseB: 0.0f, ampR: 0.15f, ampG: 0.05f, ampB: 0.2f);

        // Assert: SIMD output matches the scalar reference exactly.
        Assert.True(scalar.SequenceEqual(simd));
    }

    /// <summary>Atlas building is deterministic and produces differing samples inside the covered rectangle.</summary>
    [Fact]
    public void BuildRgb16fPixelBuffers_IsDeterministicAndVariesInsideCoveredRect()
    {
        // Arrange: construct one page and a material with independent roughness, metallic, and emissive noise.
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
            Scale = PbrOverrideScale.Identity,
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

        // Act: build the same atlas page twice from identical sources.
        var r1 = MaterialAtlasParamsBuilder.BuildRgb16fPixelBuffers(atlasPages, texturePositions, materialsByTexture);
        var r2 = MaterialAtlasParamsBuilder.BuildRgb16fPixelBuffers(atlasPages, texturePositions, materialsByTexture);

        float[] pixels1 = r1.PixelBuffersByAtlasTexId[1];
        float[] pixels2 = r2.PixelBuffersByAtlasTexId[1];

        // Assert: output is deterministic, varies inside the covered rectangle, and stays finite and normalized.
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

        // Output is a normalized material-param texture: all channels must remain in [0,1] and be finite.
        foreach (float v in pixels1)
        {
            Assert.False(float.IsNaN(v));
            Assert.False(float.IsInfinity(v));
            Assert.InRange(v, 0f, 1f);
        }
    }

    /// <summary>Generated RGB16F atlas buffers contain finite normalized values.</summary>
    [Fact]
    public void BuildRgb16fPixelBuffers_OutputIsFiniteAndClamped()
    {
        // Arrange: build a material atlas page with bounded authored parameters and noise.
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
            Scale = PbrOverrideScale.Identity,
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

        // Act: generate the RGB parameter buffer for the page.
        var r = MaterialAtlasParamsBuilder.BuildRgb16fPixelBuffers(atlasPages, texturePositions, materialsByTexture);
        float[] pixels = r.PixelBuffersByAtlasTexId[1];

        // Assert: every channel is finite and lies in the normalized output range.
        foreach (float v in pixels)
        {
            Assert.False(float.IsNaN(v));
            Assert.False(float.IsInfinity(v));
            Assert.InRange(v, 0f, 1f);
        }
    }
    #endregion

    #region Private
    /// <summary>Hashes integer values with FNV-1a for stable bitwise test diagnostics.</summary>
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

    /// <summary>Hashes the exact IEEE-754 bit patterns in a float span for stable test diagnostics.</summary>
    private static uint HashFloatBits(ReadOnlySpan<float> values)
    {
        Span<int> bits = values.Length <= 512 ? stackalloc int[values.Length] : new int[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            bits[i] = BitConverter.SingleToInt32Bits(values[i]);
        }
        return Fnv1a32(bits);
    }
    #endregion
}
