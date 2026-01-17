using System;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit.Numerics;

[Trait("Category", "Unit")]
public sealed class SimdSpanMathTests
{
    [Fact]
    public void BytesToSingles01_MatchesScalarWithinEpsilon()
    {
        byte[] src = new byte[256];
        for (int i = 0; i < src.Length; i++)
        {
            src[i] = (byte)i;
        }

        float[] dst = new float[src.Length];
        SimdSpanMath.BytesToSingles01(src, dst);

        for (int i = 0; i < src.Length; i++)
        {
            float expected = src[i] / 255f;
            Assert.Equal(expected, dst[i], precision: 6);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(24)]
    [InlineData(27)]
    public void FillInterleaved3_FillsExpectedTriplets(int length)
    {
        Span<float> buffer = length <= 256 ? stackalloc float[length] : new float[length];

        const float a0 = 0.25f;
        const float a1 = 0.5f;
        const float a2 = 0.75f;

        SimdSpanMath.FillInterleaved3(buffer, a0, a1, a2);

        Assert.Equal(0, buffer.Length % 3);
        for (int i = 0; i < buffer.Length; i += 3)
        {
            Assert.Equal(a0, buffer[i + 0]);
            Assert.Equal(a1, buffer[i + 1]);
            Assert.Equal(a2, buffer[i + 2]);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void FillInterleaved3_ThrowsOnInvalidLength(int length)
    {
        var buffer = new float[length];
        Assert.Throws<ArgumentException>(() => SimdSpanMath.FillInterleaved3(buffer, 1f, 2f, 3f));
    }

    [Fact]
    public void Clamp01_ClampsEdges_AndPreservesNaN()
    {
        Span<float> values = stackalloc float[]
        {
            -1f,
            0f,
            0.5f,
            1f,
            2f,
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity,
        };

        SimdSpanMath.Clamp01(values);

        Assert.Equal(0f, values[0]);
        Assert.Equal(0f, values[1]);
        Assert.Equal(0.5f, values[2]);
        Assert.Equal(1f, values[3]);
        Assert.Equal(1f, values[4]);
        Assert.True(float.IsNaN(values[5]));
        Assert.Equal(1f, values[6]);
        Assert.Equal(0f, values[7]);
    }
}
