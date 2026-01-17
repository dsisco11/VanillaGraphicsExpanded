using System;
using System.Numerics;
using System.Numerics.Tensors;

namespace VanillaGraphicsExpanded.Noise;

internal static class PmjSobol02
{
    private const double InvTwoTo32 = 1.0 / 4294967296.0; // 2^-32
    private const float InvTwoTo32F = 1f / 4294967296f; // 2^-32

    // Dimension 1: van der Corput in base-2 (bit reversal).
    public static uint SampleDim1(uint index) => ReverseBits32(index);

    // Dimension 2: Sobol direction numbers for primitive polynomial x^3 + x + 1.
    // Parameters: s=3, a=1, m = [1,3,5].
    public static uint SampleDim2(uint index)
    {
        // Precomputed v[1..32] direction numbers for dimension 2.
        // Built from the standard Sobol construction (Bratley-Fox) with the above polynomial.
        ReadOnlySpan<uint> v = DirectionNumbersDim2;

        uint x = 0;
        uint i = index;
        int bit = 0;

        while (i != 0)
        {
            if ((i & 1u) != 0)
            {
                x ^= v[bit];
            }

            i >>= 1;
            bit++;
        }

        return x;
    }

    /// <summary>
    /// Fills <paramref name="xBits"/> and <paramref name="yBits"/> with a progressive Sobol(0,2)
    /// sequence (32-bit fixed point), using the standard incremental update:
    /// <c>x[i] = x[i-1] XOR v[ctz(i)]</c>.
    /// </summary>
    public static void FillBitsProgressive(int count, Span<uint> xBits, Span<uint> yBits)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be > 0.");
        if (xBits.Length < count) throw new ArgumentException($"xBits must be at least {count} elements long.", nameof(xBits));
        if (yBits.Length < count) throw new ArgumentException($"yBits must be at least {count} elements long.", nameof(yBits));

        // i=0 is always 0.
        uint x = 0;
        uint y = 0;
        xBits[0] = 0;
        yBits[0] = 0;

        ReadOnlySpan<uint> v2 = DirectionNumbersDim2;

        for (int i = 1; i < count; i++)
        {
            // ctz(i) gives the index of the least-significant 1-bit.
            int c = BitOperations.TrailingZeroCount(i);

            // Dim1 direction numbers are simply 1/2,1/4,... as 32-bit fixed point.
            uint v1 = 0x80000000u >> c;

            x ^= v1;
            y ^= v2[c];

            xBits[i] = x;
            yBits[i] = y;
        }
    }

    public static float ToUnitFloat01(uint bits)
    {
        // Map 32-bit fixed-point to [0,1). Using 2^-32 keeps 1.0 excluded.
        return (float)(bits * InvTwoTo32);
    }

    public static void ToUnitFloat01(ReadOnlySpan<uint> bits, Span<float> destination)
    {
        if (destination.Length < bits.Length)
        {
            throw new ArgumentException($"Destination must be at least {bits.Length} elements long.", nameof(destination));
        }

        // Convert to float (truncating is exact for uint <= 2^24; we treat these as fixed-point bits,
        // so the conversion is an intermediate step before scaling by 2^-32).
        TensorPrimitives.ConvertTruncating<uint, float>(bits, destination);
        TensorPrimitives.Multiply(destination.Slice(0, bits.Length), InvTwoTo32F, destination.Slice(0, bits.Length));
    }

    private static uint ReverseBits32(uint v)
    {
        // Standard bit reversal via swizzles.
        v = ((v >> 1) & 0x55555555u) | ((v & 0x55555555u) << 1);
        v = ((v >> 2) & 0x33333333u) | ((v & 0x33333333u) << 2);
        v = ((v >> 4) & 0x0F0F0F0Fu) | ((v & 0x0F0F0F0Fu) << 4);
        v = ((v >> 8) & 0x00FF00FFu) | ((v & 0x00FF00FFu) << 8);
        v = (v >> 16) | (v << 16);
        return v;
    }

    private static ReadOnlySpan<uint> DirectionNumbersDim2 => new uint[32]
    {
        // v[i] stored as 32-bit integer where MSB corresponds to 1/2.
        // i=1..3 from m=[1,3,5]. Remaining computed via recurrence for s=3, a=1.
        0x80000000u, // i=1: 1/2
        0xC0000000u, // i=2: 3/4
        0xA0000000u, // i=3: 5/8
        0x50000000u,
        0xF8000000u,
        0x8C000000u,
        0xBE000000u,
        0x53000000u,
        0xF7800000u,
        0x89C00000u,
        0xBDE00000u,
        0x52B00000u,
        0xF7D80000u,
        0x89EC0000u,
        0xBDFE0000u,
        0x52BF0000u,
        0xF7DF8000u,
        0x89EFE000u,
        0xBDFEB000u,
        0x52BFD800u,
        0xF7DFEC00u,
        0x89EFFE00u,
        0xBDFEBF00u,
        0x52BFD780u,
        0xF7DFE9C0u,
        0x89EFFDE0u,
        0xBDFEBDE0u,
        0x52BF52B0u,
        0xF7D8F7D8u,
        0x89EC89ECu,
        0xBDFEBDFEu,
        0x52BF52BFu,
    };
}
