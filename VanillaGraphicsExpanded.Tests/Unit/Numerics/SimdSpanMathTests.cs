using System;
using System.Numerics.Tensors;

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
            Assert.InRange(MathF.Abs(expected - dst[i]), 0f, 1e-6f);
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
    public void TensorPrimitivesClamp_ClampsEdges_AndPreservesNaN()
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

        System.Numerics.Tensors.TensorPrimitives.Clamp(values, 0f, 1f, values);

        Assert.Equal(0f, values[0]);
        Assert.Equal(0f, values[1]);
        Assert.Equal(0.5f, values[2]);
        Assert.Equal(1f, values[3]);
        Assert.Equal(1f, values[4]);
        Assert.True(float.IsNaN(values[5]));
        Assert.Equal(1f, values[6]);
        Assert.Equal(0f, values[7]);
    }

    [Fact]
    public void MultiplyClamp01Interleaved3InPlace2D_MultipliesAndClampsPerChannel()
    {
        // 2x2 rect inside a 3-wide row stride, RGB interleaved.
        // Layout in destination span starts at rect origin.
        Span<float> dst = stackalloc float[3 * 3 * 2];

        // Fill entire span with 0.5 so we can detect clamping and scaling.
        for (int i = 0; i < dst.Length; i++) dst[i] = 0.5f;

        // Put a NaN in the rect to validate NaN preservation.
        // Rect row 0, pixel 0, channel 0.
        dst[0] = float.NaN;

        SimdSpanMath.MultiplyClamp01Interleaved3InPlace2D(
            destination3: dst,
            rectWidthPixels: 2,
            rectHeightPixels: 2,
            rowStridePixels: 3,
            mul0: 2f,
            mul1: 0.25f,
            mul2: 10f);

        // Row stride is 3 pixels -> 9 floats per row.
        const int rowStrideFloats = 9;

        for (int y = 0; y < 2; y++)
        {
            int rowBase = y * rowStrideFloats;

            for (int x = 0; x < 2; x++)
            {
                int i = rowBase + (x * 3);

                if (y == 0 && x == 0)
                {
                    Assert.True(float.IsNaN(dst[i + 0]));
                }
                else
                {
                    Assert.Equal(1f, dst[i + 0]); // 0.5 * 2 = 1 (clamped)
                }

                Assert.Equal(0.125f, dst[i + 1], precision: 6); // 0.5 * 0.25 = 0.125
                Assert.Equal(1f, dst[i + 2]); // 0.5 * 10 = 5 (clamped)
            }
        }

        // The 3rd pixel in each row is outside the rect and must remain unchanged.
        Assert.Equal(0.5f, dst[0 * rowStrideFloats + 2 * 3 + 0]);
        Assert.Equal(0.5f, dst[0 * rowStrideFloats + 2 * 3 + 1]);
        Assert.Equal(0.5f, dst[0 * rowStrideFloats + 2 * 3 + 2]);

        Assert.Equal(0.5f, dst[1 * rowStrideFloats + 2 * 3 + 0]);
        Assert.Equal(0.5f, dst[1 * rowStrideFloats + 2 * 3 + 1]);
        Assert.Equal(0.5f, dst[1 * rowStrideFloats + 2 * 3 + 2]);
    }

    [Fact]
    public void MultiplyClamp01Interleaved4InPlace2D_MultipliesAndClampsRgbAndASeparately()
    {
        // 2x2 rect inside a 3-wide row stride, RGBA interleaved.
        Span<float> dst = stackalloc float[3 * 4 * 2];

        for (int i = 0; i < dst.Length; i++) dst[i] = 0.5f;

        // Place NaNs in RGB and A of first pixel to ensure they propagate.
        dst[0] = float.NaN;
        dst[3] = float.NaN;

        SimdSpanMath.MultiplyClamp01Interleaved4InPlace2D(
            destination4: dst,
            rectWidthPixels: 2,
            rectHeightPixels: 2,
            rowStridePixels: 3,
            mulRgb: 2f,
            mulA: 0.25f);

        const int rowStrideFloats = 12; // 3 pixels * 4 floats

        for (int y = 0; y < 2; y++)
        {
            int rowBase = y * rowStrideFloats;

            for (int x = 0; x < 2; x++)
            {
                int i = rowBase + (x * 4);

                if (y == 0 && x == 0)
                {
                    Assert.True(float.IsNaN(dst[i + 0]));
                    Assert.True(float.IsNaN(dst[i + 3]));
                }
                else
                {
                    Assert.Equal(1f, dst[i + 0]);
                    Assert.Equal(1f, dst[i + 1]);
                    Assert.Equal(1f, dst[i + 2]);
                    Assert.Equal(0.125f, dst[i + 3], precision: 6);
                }
            }
        }

        // 3rd pixel in each row is outside the rect.
        Assert.Equal(0.5f, dst[0 * rowStrideFloats + 2 * 4 + 0]);
        Assert.Equal(0.5f, dst[0 * rowStrideFloats + 2 * 4 + 1]);
        Assert.Equal(0.5f, dst[0 * rowStrideFloats + 2 * 4 + 2]);
        Assert.Equal(0.5f, dst[0 * rowStrideFloats + 2 * 4 + 3]);

        Assert.Equal(0.5f, dst[1 * rowStrideFloats + 2 * 4 + 0]);
        Assert.Equal(0.5f, dst[1 * rowStrideFloats + 2 * 4 + 1]);
        Assert.Equal(0.5f, dst[1 * rowStrideFloats + 2 * 4 + 2]);
        Assert.Equal(0.5f, dst[1 * rowStrideFloats + 2 * 4 + 3]);
    }
}
