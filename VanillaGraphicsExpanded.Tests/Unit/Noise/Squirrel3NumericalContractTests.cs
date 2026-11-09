using System;
using VanillaGraphicsExpanded.Noise;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Noise;

/// <summary>Verifies scalar, SIMD, bulk-coordinate, and floating-point Squirrel3 noise contracts.</summary>
public class Squirrel3NumericalContractTests
{

    #region Public API

    #region SIMD UInt Hash Tests

    /// <summary>Each SIMD lane matches the corresponding scalar hash result.</summary>
    [Fact]
    public void HashU_Vector128_OneInput_MatchesScalar()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        Assert.SkipWhen(!(Sse2.IsSupported && Sse41.IsSupported), "SSE2+SSE4.1 not supported on this platform");

        Vector128<uint> v = Vector128.Create(0u, 1u, 123u, 0xDEADBEEFu);

        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Vector128<uint> hashed = Squirrel3Noise.HashU_Vector128_U32(v);

        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.Equal(Squirrel3Noise.HashU(v.GetElement(0)), hashed.GetElement(0));
        Assert.Equal(Squirrel3Noise.HashU(v.GetElement(1)), hashed.GetElement(1));
        Assert.Equal(Squirrel3Noise.HashU(v.GetElement(2)), hashed.GetElement(2));
        Assert.Equal(Squirrel3Noise.HashU(v.GetElement(3)), hashed.GetElement(3));
    }

    /// <summary>Each SIMD lane matches the corresponding scalar hash result.</summary>
    [Fact]
    public void HashU_Vector128_TwoInputs_MatchesScalar()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        Assert.SkipWhen(!(Sse2.IsSupported && Sse41.IsSupported), "SSE2+SSE4.1 not supported on this platform");

        Vector128<uint> v1 = Vector128.Create(0u, 1u, 123u, 0xDEADBEEFu);
        Vector128<uint> v2 = Vector128.Create(0u, 2u, 456u, 0x12345678u);

        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Vector128<uint> hashed = Squirrel3Noise.HashU_Vector128_U32(v1, v2);

        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(0), v2.GetElement(0)), hashed.GetElement(0));
        Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(1), v2.GetElement(1)), hashed.GetElement(1));
        Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(2), v2.GetElement(2)), hashed.GetElement(2));
        Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(3), v2.GetElement(3)), hashed.GetElement(3));
    }

    /// <summary>Each SIMD lane matches the corresponding scalar hash result.</summary>
    [Fact]
    public void HashU_Vector128_ThreeInputs_MatchesScalar()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        Assert.SkipWhen(!(Sse2.IsSupported && Sse41.IsSupported), "SSE2+SSE4.1 not supported on this platform");

        Vector128<uint> v1 = Vector128.Create(0u, 1u, 123u, 0xDEADBEEFu);
        Vector128<uint> v2 = Vector128.Create(0u, 2u, 456u, 0x12345678u);
        Vector128<uint> v3 = Vector128.Create(0u, 3u, 789u, 0xCAFEBABEu);

        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Vector128<uint> hashed = Squirrel3Noise.HashU_Vector128_U32(v1, v2, v3);

        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(0), v2.GetElement(0), v3.GetElement(0)), hashed.GetElement(0));
        Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(1), v2.GetElement(1), v3.GetElement(1)), hashed.GetElement(1));
        Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(2), v2.GetElement(2), v3.GetElement(2)), hashed.GetElement(2));
        Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(3), v2.GetElement(3), v3.GetElement(3)), hashed.GetElement(3));
    }

    /// <summary>Each SIMD lane matches the corresponding scalar hash result.</summary>
    [Fact]
    public void HashU_Vector256_OneInput_MatchesScalar()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        Assert.SkipWhen(!Avx2.IsSupported, "AVX2 not supported on this platform");

        Vector256<uint> v = Vector256.Create(0u, 1u, 2u, 3u, 123u, 456u, 0xDEADBEEFu, uint.MaxValue);

        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Vector256<uint> hashed = Squirrel3Noise.HashU_Vector256_U32(v);

        for (int i = 0; i < 8; i++)
        {
        // Assert: the output preserves its independent hash or coordinate-layout contract.
            Assert.Equal(Squirrel3Noise.HashU(v.GetElement(i)), hashed.GetElement(i));
        }
    }

    /// <summary>Each SIMD lane matches the corresponding scalar hash result.</summary>
    [Fact]
    public void HashU_Vector256_TwoInputs_MatchesScalar()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        Assert.SkipWhen(!Avx2.IsSupported, "AVX2 not supported on this platform");

        Vector256<uint> v1 = Vector256.Create(0u, 1u, 2u, 3u, 123u, 456u, 0xDEADBEEFu, uint.MaxValue);
        Vector256<uint> v2 = Vector256.Create(10u, 11u, 12u, 13u, 100u, 200u, 0x12345678u, uint.MaxValue);

        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Vector256<uint> hashed = Squirrel3Noise.HashU_Vector256_U32(v1, v2);

        for (int i = 0; i < 8; i++)
        {
        // Assert: the output preserves its independent hash or coordinate-layout contract.
            Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(i), v2.GetElement(i)), hashed.GetElement(i));
        }
    }

    /// <summary>Each SIMD lane matches the corresponding scalar hash result.</summary>
    [Fact]
    public void HashU_Vector256_ThreeInputs_MatchesScalar()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        Assert.SkipWhen(!Avx2.IsSupported, "AVX2 not supported on this platform");

        Vector256<uint> v1 = Vector256.Create(0u, 1u, 2u, 3u, 123u, 456u, 0xDEADBEEFu, uint.MaxValue);
        Vector256<uint> v2 = Vector256.Create(10u, 11u, 12u, 13u, 100u, 200u, 0x12345678u, uint.MaxValue);
        Vector256<uint> v3 = Vector256.Create(20u, 21u, 22u, 23u, 300u, 400u, 0xCAFEBABEu, uint.MaxValue);

        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Vector256<uint> hashed = Squirrel3Noise.HashU_Vector256_U32(v1, v2, v3);

        for (int i = 0; i < 8; i++)
        {
        // Assert: the output preserves its independent hash or coordinate-layout contract.
            Assert.Equal(Squirrel3Noise.HashU(v1.GetElement(i), v2.GetElement(i), v3.GetElement(i)), hashed.GetElement(i));
        }
    }

    #endregion


    #region UInt Hash Tests

    /// <summary>The scalar hash matches the independent 32-bit reference calculation.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(123u)]
    [InlineData(0xDEADBEEFu)]
    [InlineData(uint.MaxValue)]
    public void HashU_OneInput_MatchesReference(uint v1)
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint expected = ReferenceHashU(v1);
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        uint actual = Squirrel3Noise.HashU(v1);
        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.Equal(expected, actual);
    }

    /// <summary>The scalar hash matches the independent 32-bit reference calculation.</summary>
    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(1u, 0u)]
    [InlineData(0u, 1u)]
    [InlineData(123u, 456u)]
    [InlineData(0xDEADBEEFu, 0x12345678u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void HashU_TwoInputs_MatchesReference(uint v1, uint v2)
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint expected = ReferenceHashU(v1, v2);
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        uint actual = Squirrel3Noise.HashU(v1, v2);
        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.Equal(expected, actual);
    }

    /// <summary>The scalar hash matches the independent 32-bit reference calculation.</summary>
    [Theory]
    [InlineData(0u, 0u, 0u)]
    [InlineData(1u, 0u, 0u)]
    [InlineData(0u, 1u, 0u)]
    [InlineData(0u, 0u, 1u)]
    [InlineData(123u, 456u, 789u)]
    [InlineData(0xDEADBEEFu, 0x12345678u, 0xCAFEBABEu)]
    [InlineData(uint.MaxValue, uint.MaxValue, uint.MaxValue)]
    public void HashU_ThreeInputs_MatchesReference(uint v1, uint v2, uint v3)
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint expected = ReferenceHashU(v1, v2, v3);
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        uint actual = Squirrel3Noise.HashU(v1, v2, v3);
        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.Equal(expected, actual);
    }

    #endregion

    #region Float Tests

    /// <summary>The normalized scalar noise value matches the reference hash mapping.</summary>
    [Fact]
    public void Noise01_OneArg_MatchesReferenceHashNormalization_ForZeroSeed()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint hashed = ReferenceHashU(0u, 0u, 0u);
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        float expected = hashed / Squirrel3Noise.FloatMax;
        float actual = Squirrel3Noise.Noise01(0u);

        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.Equal((double)expected, (double)actual, precision: 7);
    }

    /// <summary>The normalized scalar noise value matches the reference hash mapping.</summary>
    [Fact]
    public void Noise01_OneArg_MatchesReferenceHashNormalization_ForMaxSeed()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint hashed = ReferenceHashU(uint.MaxValue, 0u, 0u);
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        float expected = hashed / Squirrel3Noise.FloatMax;
        float actual = Squirrel3Noise.Noise01(uint.MaxValue);

        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.Equal((double)expected, (double)actual, precision: 7);
    }

    /// <summary>The signed noise output remains in the documented unit interval.</summary>
    [Fact]
    public void NoiseSigned1_ZeroSeed_IsMinusOneToOneRange()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        float v = Squirrel3Noise.NoiseSigned1(0u);
        // Assert: the output preserves its independent hash or coordinate-layout contract.
        Assert.InRange(v, -1.0f, 1.0f);
    }

    #endregion

    #region Bulk Noise (Span) Tests

    /// <summary>The one-dimensional span follows sequential coordinate order.</summary>
    [Fact]
    public void NoiseU32_1D_FillsSequentialValues()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint seed = 123u;
        uint pos = 1000u;

        uint[] buf = new uint[64];
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Squirrel3Noise.NoiseU32(seed, pos, buf);

        for (int i = 0; i < buf.Length; i++)
        {
            uint expected = Squirrel3Noise.HashU(unchecked(seed + pos + (uint)i), 0u, 0u);
        // Assert: the output preserves its independent hash or coordinate-layout contract.
            Assert.Equal(expected, buf[i]);
        }
    }

    /// <summary>The two-dimensional span follows the established X-major coordinate order.</summary>
    [Fact]
    public void NoiseU32_2D_FillsXMajorOrder()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint seed = 42u;
        uint posX = 7u;
        uint posY = 9u;
        int size = 9;

        uint[] buf = new uint[size * size];
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Squirrel3Noise.NoiseU32(seed, posX, posY, size, buf);

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                int idx = (x * size) + y;
                uint expected = Squirrel3Noise.HashU(unchecked(seed + posX + (uint)x), unchecked(posY + (uint)y), 0u);
        // Assert: the output preserves its independent hash or coordinate-layout contract.
                Assert.Equal(expected, buf[idx]);
            }
        }
    }

    /// <summary>The three-dimensional span follows the established X-major coordinate order.</summary>
    [Fact]
    public void NoiseU32_3D_FillsXMajorOrder()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint seed = 999u;
        uint posX = 1u;
        uint posY = 2u;
        uint posZ = 3u;
        int size = 5;

        uint[] buf = new uint[size * size * size];
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Squirrel3Noise.NoiseU32(seed, posX, posY, posZ, size, buf);

        int stride1 = size;
        int stride2 = size * size;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    int idx = (x * stride2) + (y * stride1) + z;
                    uint expected = Squirrel3Noise.HashU(unchecked(seed + posX + (uint)x), unchecked(posY + (uint)y), unchecked(posZ + (uint)z));
        // Assert: the output preserves its independent hash or coordinate-layout contract.
                    Assert.Equal(expected, buf[idx]);
                }
            }
        }
    }

    /// <summary>Bulk floating noise matches normalization of its unsigned hash values.</summary>
    [Fact]
    public void Noise01_1D_MatchesU32Normalization()
    {
        // Arrange: prepare vector lanes, scalar inputs, or a destination span.
        uint seed = 123u;
        uint pos = 1000u;

        float[] buf = new float[64];
        // Act: evaluate the requested scalar, SIMD, or bulk noise operation.
        Squirrel3Noise.Noise01(seed, pos, buf);

        for (int i = 0; i < buf.Length; i++)
        {
            uint u = Squirrel3Noise.HashU(unchecked(seed + pos + (uint)i), 0u, 0u);
            float expected = u / Squirrel3Noise.FloatMax;
        // Assert: the output preserves its independent hash or coordinate-layout contract.
            Assert.Equal((double)expected, (double)buf[i], precision: 7);
        }
    }

    #endregion

    #endregion

    #region Private

    #region Reference Implementations

    /// <summary>Reduces a widened value to its low unsigned 32 bits.</summary>
    private static uint U32(ulong v) => (uint)(v & 0xFFFF_FFFFul);

    /// <summary>Computes the one-coordinate hash through an independent widened reference sequence.</summary>
    private static uint ReferenceHashU(uint v1)
    {
        ulong mangled = v1;
        mangled = (mangled * Squirrel3Noise.BitNoise1) & 0xFFFF_FFFFul;
        mangled ^= (mangled >> 8);
        return U32(mangled);
    }

    /// <summary>Computes the two-coordinate hash through explicit unsigned wraparound.</summary>
    private static uint ReferenceHashU(uint v1, uint v2)
    {
        ulong mangled = (ulong)v1 + ((ulong)v2 * Squirrel3Noise.PrimeU1);
        mangled &= 0xFFFF_FFFFul;

        mangled = (mangled * Squirrel3Noise.BitNoise1) & 0xFFFF_FFFFul;
        mangled ^= (mangled >> 8);

        mangled = (mangled + Squirrel3Noise.BitNoise2) & 0xFFFF_FFFFul;
        mangled ^= (mangled << 8) & 0xFFFF_FFFFul;

        return U32(mangled);
    }

    /// <summary>Computes the three-coordinate hash through explicit unsigned wraparound.</summary>
    private static uint ReferenceHashU(uint v1, uint v2, uint v3)
    {
        ulong mangled = (ulong)v1 + ((ulong)v2 * Squirrel3Noise.PrimeU1) + ((ulong)v3 * Squirrel3Noise.PrimeU2);
        mangled &= 0xFFFF_FFFFul;

        mangled = (mangled * Squirrel3Noise.BitNoise1) & 0xFFFF_FFFFul;
        mangled ^= (mangled >> 8);

        mangled = (mangled + Squirrel3Noise.BitNoise2) & 0xFFFF_FFFFul;
        mangled ^= (mangled << 8) & 0xFFFF_FFFFul;

        mangled = (mangled * Squirrel3Noise.BitNoise3) & 0xFFFF_FFFFul;
        mangled ^= (mangled >> 8);

        return U32(mangled);
    }

    #endregion
    #endregion
}
