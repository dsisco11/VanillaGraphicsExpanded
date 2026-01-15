using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Noise;

/// <summary>
/// CPU implementation of the Squirrel3 hash/noise functions used in the shader include
/// <c>assets/vanillagraphicsexpanded/shaders/includes/squirrel3.glsl</c>.
/// </summary>
internal static class Squirrel3Noise
{
    #region Constants (match GLSL)

    internal const uint PrimeU1 = 198_491_317u;
    internal const uint PrimeU2 = 6_542_989u;
    internal const uint PrimeU3 = 786_433u;

    internal const uint BitNoise1 = 3_039_394_381u; // 0xB5297A4D
    internal const uint BitNoise2 = 1_759_714_724u; // 0x68E31DA4
    internal const uint BitNoise3 = 458_671_337u;   // 0x1B56C4E9

    internal const float FloatMax = 4_294_967_295.0f;

    #endregion

    #region Shared Mangling Steps

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Mangle2(uint mangled)
    {
        // Matches the post-mix steps of Squirrel3HashU(v1, v2) in the GLSL include.
        mangled = unchecked(mangled * BitNoise1);
        mangled = mangled ^ (mangled >> 8);
        mangled = unchecked(mangled + BitNoise2);
        mangled = mangled ^ (mangled << 8);
        return mangled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Mangle3(uint mangled)
    {
        // Matches the post-mix steps of Squirrel3HashU(v1, v2, v3) in the GLSL include.
        mangled = unchecked(mangled * BitNoise1);
        mangled = mangled ^ (mangled >> 8);
        mangled = unchecked(mangled + BitNoise2);
        mangled = mangled ^ (mangled << 8);
        mangled = unchecked(mangled * BitNoise3);
        mangled = mangled ^ (mangled >> 8);
        return mangled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> Mangle2_Vector128_U32(Vector128<uint> mangled)
    {
        mangled = MultiplyLow_Vector128_U32(mangled, Vector128.Create(BitNoise1));
        mangled = Sse2.Xor(mangled, Sse2.ShiftRightLogical(mangled, 8));
        mangled = Sse2.Add(mangled, Vector128.Create(BitNoise2));
        mangled = Sse2.Xor(mangled, Sse2.ShiftLeftLogical(mangled, 8));
        return mangled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> Mangle3_Vector128_U32(Vector128<uint> mangled)
    {
        mangled = MultiplyLow_Vector128_U32(mangled, Vector128.Create(BitNoise1));
        mangled = Sse2.Xor(mangled, Sse2.ShiftRightLogical(mangled, 8));
        mangled = Sse2.Add(mangled, Vector128.Create(BitNoise2));
        mangled = Sse2.Xor(mangled, Sse2.ShiftLeftLogical(mangled, 8));
        mangled = MultiplyLow_Vector128_U32(mangled, Vector128.Create(BitNoise3));
        mangled = Sse2.Xor(mangled, Sse2.ShiftRightLogical(mangled, 8));
        return mangled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> Mangle2_Vector256_U32(Vector256<uint> mangled)
    {
        mangled = MultiplyLow_Vector256_U32(mangled, Vector256.Create(BitNoise1));
        mangled = Avx2.Xor(mangled, Avx2.ShiftRightLogical(mangled, 8));
        mangled = Avx2.Add(mangled, Vector256.Create(BitNoise2));
        mangled = Avx2.Xor(mangled, Avx2.ShiftLeftLogical(mangled, 8));
        return mangled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> Mangle3_Vector256_U32(Vector256<uint> mangled)
    {
        mangled = MultiplyLow_Vector256_U32(mangled, Vector256.Create(BitNoise1));
        mangled = Avx2.Xor(mangled, Avx2.ShiftRightLogical(mangled, 8));
        mangled = Avx2.Add(mangled, Vector256.Create(BitNoise2));
        mangled = Avx2.Xor(mangled, Avx2.ShiftLeftLogical(mangled, 8));
        mangled = MultiplyLow_Vector256_U32(mangled, Vector256.Create(BitNoise3));
        mangled = Avx2.Xor(mangled, Avx2.ShiftRightLogical(mangled, 8));
        return mangled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> ConvertU32ToF32_Vector128(Vector128<uint> u)
    {
        // Unsigned int -> float conversion without unsigned-convert intrinsics:
        //   float(u) = float((u >> 1) | (u & 1)) * 2
        // Keeps the intermediate within [0, 0x7FFFFFFF] so signed conversion is valid.
        Vector128<uint> half = Sse2.ShiftRightLogical(u, 1);
        Vector128<uint> lsb = Sse2.And(u, Vector128.Create(1u));
        Vector128<uint> adjusted = Sse2.Or(half, lsb);
        Vector128<float> f = Sse2.ConvertToVector128Single(adjusted.AsInt32());
        return Sse.Multiply(f, Vector128.Create(2.0f));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> ConvertU32ToF32_Vector256(Vector256<uint> u)
    {
        // Unsigned int -> float conversion without unsigned-convert intrinsics:
        //   float(u) = float((u >> 1) | (u & 1)) * 2
        // Keeps the intermediate within [0, 0x7FFFFFFF] so signed conversion is valid.
        Vector256<uint> half = Avx2.ShiftRightLogical(u, 1);
        Vector256<uint> lsb = Avx2.And(u, Vector256.Create(1u));
        Vector256<uint> adjusted = Avx2.Or(half, lsb);
        Vector256<float> f = Avx.ConvertToVector256Single(adjusted.AsInt32());
        return Avx.Multiply(f, Vector256.Create(2.0f));
    }

    #endregion

    #region SIMD UInt Hash Functions

    /// <summary>
    /// Vectorized Squirrel3 hash for one uint input per lane.
    /// Uses SSE2+SSE4.1 (Vector128) when supported; otherwise falls back to scalar per-lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<uint> HashU_Vector128_U32(Vector128<uint> v1)
    {
        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            Vector128<uint> mangled = v1;
            mangled = MultiplyLow_Vector128_U32(mangled, Vector128.Create(BitNoise1));
            mangled = Sse2.Xor(mangled, Sse2.ShiftRightLogical(mangled, 8));
            return mangled;
        }

        return Vector128.Create(
            HashU(v1.GetElement(0)),
            HashU(v1.GetElement(1)),
            HashU(v1.GetElement(2)),
            HashU(v1.GetElement(3)));
    }

    /// <summary>
    /// Vectorized Squirrel3 hash for two uint inputs per lane.
    /// Uses SSE2+SSE4.1 (Vector128) when supported; otherwise falls back to scalar per-lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<uint> HashU_Vector128_U32(Vector128<uint> v1, Vector128<uint> v2)
    {
        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            Vector128<uint> mixed = Sse2.Add(v1, MultiplyLow_Vector128_U32(v2, Vector128.Create(PrimeU1)));
            return Mangle2_Vector128_U32(mixed);
        }

        return Vector128.Create(
            HashU(v1.GetElement(0), v2.GetElement(0)),
            HashU(v1.GetElement(1), v2.GetElement(1)),
            HashU(v1.GetElement(2), v2.GetElement(2)),
            HashU(v1.GetElement(3), v2.GetElement(3)));
    }

    /// <summary>
    /// Vectorized Squirrel3 hash for three uint inputs per lane.
    /// Uses SSE2+SSE4.1 (Vector128) when supported; otherwise falls back to scalar per-lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<uint> HashU_Vector128_U32(Vector128<uint> v1, Vector128<uint> v2, Vector128<uint> v3)
    {
        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            Vector128<uint> mixed = Sse2.Add(v1, MultiplyLow_Vector128_U32(v2, Vector128.Create(PrimeU1)));
            mixed = Sse2.Add(mixed, MultiplyLow_Vector128_U32(v3, Vector128.Create(PrimeU2)));
            return Mangle3_Vector128_U32(mixed);
        }

        return Vector128.Create(
            HashU(v1.GetElement(0), v2.GetElement(0), v3.GetElement(0)),
            HashU(v1.GetElement(1), v2.GetElement(1), v3.GetElement(1)),
            HashU(v1.GetElement(2), v2.GetElement(2), v3.GetElement(2)),
            HashU(v1.GetElement(3), v2.GetElement(3), v3.GetElement(3)));
    }

    /// <summary>
    /// Vectorized Squirrel3 hash for one uint input per lane.
    /// Uses AVX2 (Vector256) when supported; otherwise falls back to scalar per-lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector256<uint> HashU_Vector256_U32(Vector256<uint> v1)
    {
        if (Avx2.IsSupported)
        {
            Vector256<uint> mangled = v1;
            mangled = MultiplyLow_Vector256_U32(mangled, Vector256.Create(BitNoise1));
            mangled = Avx2.Xor(mangled, Avx2.ShiftRightLogical(mangled, 8));
            return mangled;
        }

        return Vector256.Create(
            HashU(v1.GetElement(0)),
            HashU(v1.GetElement(1)),
            HashU(v1.GetElement(2)),
            HashU(v1.GetElement(3)),
            HashU(v1.GetElement(4)),
            HashU(v1.GetElement(5)),
            HashU(v1.GetElement(6)),
            HashU(v1.GetElement(7)));
    }

    /// <summary>
    /// Vectorized Squirrel3 hash for two uint inputs per lane.
    /// Uses AVX2 (Vector256) when supported; otherwise falls back to scalar per-lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector256<uint> HashU_Vector256_U32(Vector256<uint> v1, Vector256<uint> v2)
    {
        if (Avx2.IsSupported)
        {
            Vector256<uint> mixed = Avx2.Add(v1, MultiplyLow_Vector256_U32(v2, Vector256.Create(PrimeU1)));
            return Mangle2_Vector256_U32(mixed);
        }

        return Vector256.Create(
            HashU(v1.GetElement(0), v2.GetElement(0)),
            HashU(v1.GetElement(1), v2.GetElement(1)),
            HashU(v1.GetElement(2), v2.GetElement(2)),
            HashU(v1.GetElement(3), v2.GetElement(3)),
            HashU(v1.GetElement(4), v2.GetElement(4)),
            HashU(v1.GetElement(5), v2.GetElement(5)),
            HashU(v1.GetElement(6), v2.GetElement(6)),
            HashU(v1.GetElement(7), v2.GetElement(7)));
    }

    /// <summary>
    /// Vectorized Squirrel3 hash for three uint inputs per lane.
    /// Uses AVX2 (Vector256) when supported; otherwise falls back to scalar per-lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector256<uint> HashU_Vector256_U32(Vector256<uint> v1, Vector256<uint> v2, Vector256<uint> v3)
    {
        if (Avx2.IsSupported)
        {
            Vector256<uint> mixed = Avx2.Add(v1, MultiplyLow_Vector256_U32(v2, Vector256.Create(PrimeU1)));
            mixed = Avx2.Add(mixed, MultiplyLow_Vector256_U32(v3, Vector256.Create(PrimeU2)));
            return Mangle3_Vector256_U32(mixed);
        }

        return Vector256.Create(
            HashU(v1.GetElement(0), v2.GetElement(0), v3.GetElement(0)),
            HashU(v1.GetElement(1), v2.GetElement(1), v3.GetElement(1)),
            HashU(v1.GetElement(2), v2.GetElement(2), v3.GetElement(2)),
            HashU(v1.GetElement(3), v2.GetElement(3), v3.GetElement(3)),
            HashU(v1.GetElement(4), v2.GetElement(4), v3.GetElement(4)),
            HashU(v1.GetElement(5), v2.GetElement(5), v3.GetElement(5)),
            HashU(v1.GetElement(6), v2.GetElement(6), v3.GetElement(6)),
            HashU(v1.GetElement(7), v2.GetElement(7), v3.GetElement(7)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> MultiplyLow_Vector128_U32(Vector128<uint> a, Vector128<uint> b)
    {
        // Sse41.MultiplyLow is defined for signed int, but the low 32 bits are identical for uint.
        Vector128<int> ai = a.AsInt32();
        Vector128<int> bi = b.AsInt32();
        Vector128<int> prod = Sse41.MultiplyLow(ai, bi);
        return prod.AsUInt32();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> MultiplyLow_Vector256_U32(Vector256<uint> a, Vector256<uint> b)
    {
        // Avx2.MultiplyLow is defined for signed int, but the low 32 bits are identical for uint.
        Vector256<int> ai = a.AsInt32();
        Vector256<int> bi = b.AsInt32();
        Vector256<int> prod = Avx2.MultiplyLow(ai, bi);
        return prod.AsUInt32();
    }

    #endregion

    #region UInt Hash Functions

    /// <summary>
    /// Squirrel3 hash function for one uint input.
    /// Matches <c>uint Squirrel3HashU(uint v1)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashU(uint v1)
    {
        uint mangled = v1;
        mangled = unchecked(mangled * BitNoise1);
        mangled = mangled ^ (mangled >> 8);
        return mangled;
    }

    /// <summary>
    /// Squirrel3 hash function for two uint inputs.
    /// Matches <c>uint Squirrel3HashU(uint v1, uint v2)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashU(uint v1, uint v2)
    {
        uint mangled = unchecked(v1 + (v2 * PrimeU1));
        return Mangle2(mangled);
    }

    /// <summary>
    /// Squirrel3 hash function for three uint inputs.
    /// Matches <c>uint Squirrel3HashU(uint v1, uint v2, uint v3)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashU(uint v1, uint v2, uint v3)
    {
        uint mangled = unchecked(v1 + (v2 * PrimeU1) + (v3 * PrimeU2));
        return Mangle3(mangled);
    }

    #endregion

    #region Float Hash Functions

    /// <summary>
    /// Hashes a single uint seed and returns a float in [0, 1].
    /// Matches <c>float Squirrel3HashF(uint seed)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Noise01(uint seed)
    {
        uint hashed = HashU(seed, 0u, 0u);
        return hashed / FloatMax;
    }

    /// <summary>
    /// Hashes two uint inputs and returns a float in [0, 1].
    /// Matches <c>float Squirrel3HashF(uint v1, uint v2)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Noise01(uint v1, uint v2)
    {
        uint hashed = HashU(v1, v2);
        return hashed / FloatMax;
    }

    /// <summary>
    /// Hashes three uint inputs and returns a float in [0, 1].
    /// Matches <c>float Squirrel3HashF(uint v1, uint v2, uint v3)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Noise01(uint v1, uint v2, uint v3)
    {
        uint hashed = HashU(v1, v2, v3);
        return hashed / FloatMax;
    }

    /// <summary>
    /// Hashes three int inputs by reinterpreting them as uints (as done in GLSL via <c>uint(v)</c>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Noise01(int v1, int v2, int v3)
    {
        uint hashed = HashU(unchecked((uint)v1), unchecked((uint)v2), unchecked((uint)v3));
        return hashed / FloatMax;
    }

    /// <summary>
    /// Returns signed noise in [-1, 1] based on <see cref="Noise01(uint)"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NoiseSigned1(uint seed) => (Noise01(seed) * 2f) - 1f;

    #endregion

    #region Bulk Noise Generation (Span)

    /// <summary>
    /// Fills <paramref name="buffer"/> with deterministic Squirrel3 uint hashes for a 1D sequence.
    /// Uses (seed + (pos + i), 0, 0) as the (v1,v2,v3) inputs.
    /// </summary>
    public static void NoiseU32(uint seed, uint pos, Span<uint> buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        // Vectorized inner loop closely mirrors the C++ approach:
        //   offsetVec = [0..W-1], step=W, base=pos
        //   v1 = seed + (base + offset)
        //   v2=v3=0
        if (Avx2.IsSupported)
        {
            ref uint dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector256<uint> posVec = Vector256.Create(pos);
            Vector256<uint> seedVec = Vector256.Create(seed);
            Vector256<uint> v2 = Vector256.Create(0u);
            Vector256<uint> v3 = Vector256.Create(0u);
            Vector256<uint> offset = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);
            Vector256<uint> step = Vector256.Create(8u);

            int i = 0;
            for (; i <= buffer.Length - 8; i += 8)
            {
                Vector256<uint> x = Avx2.Add(posVec, offset);
                Vector256<uint> v1 = Avx2.Add(seedVec, x);
                Vector256<uint> hashed = HashU_Vector256_U32(v1, v2, v3);

                Unsafe.WriteUnaligned(
                    ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref dstRef, i)),
                    hashed);

                offset = Avx2.Add(offset, step);
            }

            for (; i < buffer.Length; i++)
            {
                buffer[i] = HashU(unchecked(seed + pos + (uint)i), 0u, 0u);
            }

            return;
        }

        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            ref uint dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector128<uint> posVec = Vector128.Create(pos);
            Vector128<uint> seedVec = Vector128.Create(seed);
            Vector128<uint> v2 = Vector128.Create(0u);
            Vector128<uint> v3 = Vector128.Create(0u);
            Vector128<uint> offset = Vector128.Create(0u, 1u, 2u, 3u);
            Vector128<uint> step = Vector128.Create(4u);

            int i = 0;
            for (; i <= buffer.Length - 4; i += 4)
            {
                Vector128<uint> x = Sse2.Add(posVec, offset);
                Vector128<uint> v1 = Sse2.Add(seedVec, x);
                Vector128<uint> hashed = HashU_Vector128_U32(v1, v2, v3);

                Unsafe.WriteUnaligned(
                    ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref dstRef, i)),
                    hashed);

                offset = Sse2.Add(offset, step);
            }

            for (; i < buffer.Length; i++)
            {
                buffer[i] = HashU(unchecked(seed + pos + (uint)i), 0u, 0u);
            }

            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = HashU(unchecked(seed + pos + (uint)i), 0u, 0u);
        }
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with deterministic Squirrel3 uint hashes for a 2D grid.
    /// Writes in x-major order: idx = x * size + y.
    /// Uses (seed + (posX + x), (posY + y), 0) as the (v1,v2,v3) inputs.
    /// </summary>
    public static void NoiseU32(uint seed, uint posX, uint posY, int size, Span<uint> buffer)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (size == 0)
        {
            return;
        }

        int required = checked(size * size);
        if (buffer.Length < required)
        {
            throw new ArgumentException($"Buffer must be at least {required} elements long.", nameof(buffer));
        }

        uint uSize = (uint)size;

        if (Avx2.IsSupported)
        {
            ref uint dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector256<uint> seedVec = Vector256.Create(seed);
            Vector256<uint> posYVec = Vector256.Create(posY);
            Vector256<uint> zeros = Vector256.Create(0u);
            Vector256<uint> yOffsets0 = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);

            for (uint x = 0; x < uSize; x++)
            {
                Vector256<uint> v1 = Vector256.Create(unchecked(seed + posX + x));

                int rowBase = checked((int)(x * uSize));
                int y = 0;
                for (; y <= size - 8; y += 8)
                {
                    Vector256<uint> yVec = Avx2.Add(posYVec, Avx2.Add(yOffsets0, Vector256.Create((uint)y)));
                    Vector256<uint> hashed = HashU_Vector256_U32(v1, yVec, zeros);

                    Unsafe.WriteUnaligned(
                        ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref dstRef, rowBase + y)),
                        hashed);
                }

                for (; y < size; y++)
                {
                    buffer[rowBase + y] = HashU(unchecked(seed + posX + x), unchecked(posY + (uint)y), 0u);
                }
            }

            return;
        }

        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            ref uint dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector128<uint> posYVec = Vector128.Create(posY);
            Vector128<uint> zeros = Vector128.Create(0u);
            Vector128<uint> yOffsets0 = Vector128.Create(0u, 1u, 2u, 3u);

            for (uint x = 0; x < uSize; x++)
            {
                Vector128<uint> v1 = Vector128.Create(unchecked(seed + posX + x));

                int rowBase = checked((int)(x * uSize));
                int y = 0;
                for (; y <= size - 4; y += 4)
                {
                    Vector128<uint> yVec = Sse2.Add(posYVec, Sse2.Add(yOffsets0, Vector128.Create((uint)y)));
                    Vector128<uint> hashed = HashU_Vector128_U32(v1, yVec, zeros);

                    Unsafe.WriteUnaligned(
                        ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref dstRef, rowBase + y)),
                        hashed);
                }

                for (; y < size; y++)
                {
                    buffer[rowBase + y] = HashU(unchecked(seed + posX + x), unchecked(posY + (uint)y), 0u);
                }
            }

            return;
        }

        for (uint x = 0; x < uSize; x++)
        {
            int rowBase = checked((int)(x * uSize));
            for (uint y = 0; y < uSize; y++)
            {
                buffer[rowBase + (int)y] = HashU(unchecked(seed + posX + x), unchecked(posY + y), 0u);
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with deterministic Squirrel3 uint hashes for a 3D grid.
    /// Writes in x-major order: idx = x * size*size + y * size + z.
    /// Uses (seed + (posX + x), (posY + y), (posZ + z)) as the (v1,v2,v3) inputs.
    /// </summary>
    public static void NoiseU32(uint seed, uint posX, uint posY, uint posZ, int size, Span<uint> buffer)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (size == 0)
        {
            return;
        }

        int required = checked(size * size * size);
        if (buffer.Length < required)
        {
            throw new ArgumentException($"Buffer must be at least {required} elements long.", nameof(buffer));
        }

        uint uSize = (uint)size;
        uint stride1 = uSize;
        uint stride2 = checked(uSize * uSize);

        if (Avx2.IsSupported)
        {
            ref uint dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector256<uint> posZVec = Vector256.Create(posZ);
            Vector256<uint> zOffsets0 = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);

            for (uint x = 0; x < uSize; x++)
            {
                Vector256<uint> v1 = Vector256.Create(unchecked(seed + posX + x));

                for (uint y = 0; y < uSize; y++)
                {
                    Vector256<uint> v2 = Vector256.Create(unchecked(posY + y));

                    int sliceBase = checked((int)(x * stride2 + y * stride1));
                    int z = 0;
                    for (; z <= size - 8; z += 8)
                    {
                        Vector256<uint> zVec = Avx2.Add(posZVec, Avx2.Add(zOffsets0, Vector256.Create((uint)z)));
                        Vector256<uint> hashed = HashU_Vector256_U32(v1, v2, zVec);

                        Unsafe.WriteUnaligned(
                            ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref dstRef, sliceBase + z)),
                            hashed);
                    }

                    for (; z < size; z++)
                    {
                        buffer[sliceBase + z] = HashU(unchecked(seed + posX + x), unchecked(posY + y), unchecked(posZ + (uint)z));
                    }
                }
            }

            return;
        }

        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            ref uint dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector128<uint> posZVec = Vector128.Create(posZ);
            Vector128<uint> zOffsets0 = Vector128.Create(0u, 1u, 2u, 3u);

            for (uint x = 0; x < uSize; x++)
            {
                Vector128<uint> v1 = Vector128.Create(unchecked(seed + posX + x));

                for (uint y = 0; y < uSize; y++)
                {
                    Vector128<uint> v2 = Vector128.Create(unchecked(posY + y));

                    int sliceBase = checked((int)(x * stride2 + y * stride1));
                    int z = 0;
                    for (; z <= size - 4; z += 4)
                    {
                        Vector128<uint> zVec = Sse2.Add(posZVec, Sse2.Add(zOffsets0, Vector128.Create((uint)z)));
                        Vector128<uint> hashed = HashU_Vector128_U32(v1, v2, zVec);

                        Unsafe.WriteUnaligned(
                            ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref dstRef, sliceBase + z)),
                            hashed);
                    }

                    for (; z < size; z++)
                    {
                        buffer[sliceBase + z] = HashU(unchecked(seed + posX + x), unchecked(posY + y), unchecked(posZ + (uint)z));
                    }
                }
            }

            return;
        }

        for (uint x = 0; x < uSize; x++)
        {
            for (uint y = 0; y < uSize; y++)
            {
                int sliceBase = checked((int)(x * stride2 + y * stride1));
                for (uint z = 0; z < uSize; z++)
                {
                    buffer[sliceBase + (int)z] = HashU(unchecked(seed + posX + x), unchecked(posY + y), unchecked(posZ + z));
                }
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with deterministic Squirrel3 noise floats in [0, 1] for a 1D sequence.
    /// </summary>
    public static void Noise01(uint seed, uint pos, Span<float> buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        if (Avx2.IsSupported)
        {
            ref float dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector256<uint> posVec = Vector256.Create(pos);
            Vector256<uint> seedVec = Vector256.Create(seed);
            Vector256<uint> v2 = Vector256.Create(0u);
            Vector256<uint> v3 = Vector256.Create(0u);
            Vector256<uint> offset = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);
            Vector256<uint> step = Vector256.Create(8u);
            Vector256<float> denom = Vector256.Create(FloatMax);

            int i = 0;
            for (; i <= buffer.Length - 8; i += 8)
            {
                Vector256<uint> x = Avx2.Add(posVec, offset);
                Vector256<uint> v1 = Avx2.Add(seedVec, x);
                Vector256<uint> hashed = HashU_Vector256_U32(v1, v2, v3);

                Vector256<float> f = ConvertU32ToF32_Vector256(hashed);
                Vector256<float> norm = Avx.Divide(f, denom);

                Unsafe.WriteUnaligned(
                    ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i)),
                    norm);

                offset = Avx2.Add(offset, step);
            }

            for (; i < buffer.Length; i++)
            {
                uint u = HashU(unchecked(seed + pos + (uint)i), 0u, 0u);
                buffer[i] = u / FloatMax;
            }

            return;
        }

        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            ref float dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector128<uint> posVec = Vector128.Create(pos);
            Vector128<uint> seedVec = Vector128.Create(seed);
            Vector128<uint> v2 = Vector128.Create(0u);
            Vector128<uint> v3 = Vector128.Create(0u);
            Vector128<uint> offset = Vector128.Create(0u, 1u, 2u, 3u);
            Vector128<uint> step = Vector128.Create(4u);
            Vector128<float> denom = Vector128.Create(FloatMax);

            int i = 0;
            for (; i <= buffer.Length - 4; i += 4)
            {
                Vector128<uint> x = Sse2.Add(posVec, offset);
                Vector128<uint> v1 = Sse2.Add(seedVec, x);
                Vector128<uint> hashed = HashU_Vector128_U32(v1, v2, v3);

                Vector128<float> f = ConvertU32ToF32_Vector128(hashed);
                Vector128<float> norm = Sse.Divide(f, denom);

                Unsafe.WriteUnaligned(
                    ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, i)),
                    norm);

                offset = Sse2.Add(offset, step);
            }

            for (; i < buffer.Length; i++)
            {
                uint u = HashU(unchecked(seed + pos + (uint)i), 0u, 0u);
                buffer[i] = u / FloatMax;
            }

            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            uint u = HashU(unchecked(seed + pos + (uint)i), 0u, 0u);
            buffer[i] = u / FloatMax;
        }
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with deterministic Squirrel3 noise floats in [0, 1] for a 2D grid.
    /// </summary>
    public static void Noise01(uint seed, uint posX, uint posY, int size, Span<float> buffer)
    {
        int required = checked(size * size);
        if (buffer.Length < required)
        {
            throw new ArgumentException($"Buffer must be at least {required} elements long.", nameof(buffer));
        }

        if (size == 0)
        {
            return;
        }

        uint uSize = (uint)size;

        if (Avx2.IsSupported)
        {
            ref float dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector256<uint> posYVec = Vector256.Create(posY);
            Vector256<uint> zeros = Vector256.Create(0u);
            Vector256<uint> yOffsets0 = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);
            Vector256<float> denom = Vector256.Create(FloatMax);

            for (uint x = 0; x < uSize; x++)
            {
                Vector256<uint> v1 = Vector256.Create(unchecked(seed + posX + x));
                int rowBase = checked((int)(x * uSize));

                int y = 0;
                for (; y <= size - 8; y += 8)
                {
                    Vector256<uint> yVec = Avx2.Add(posYVec, Avx2.Add(yOffsets0, Vector256.Create((uint)y)));
                    Vector256<uint> hashed = HashU_Vector256_U32(v1, yVec, zeros);

                    Vector256<float> f = ConvertU32ToF32_Vector256(hashed);
                    Vector256<float> norm = Avx.Divide(f, denom);

                    Unsafe.WriteUnaligned(
                        ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, rowBase + y)),
                        norm);
                }

                for (; y < size; y++)
                {
                    uint u = HashU(unchecked(seed + posX + x), unchecked(posY + (uint)y), 0u);
                    buffer[rowBase + y] = u / FloatMax;
                }
            }

            return;
        }

        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            ref float dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector128<uint> posYVec = Vector128.Create(posY);
            Vector128<uint> zeros = Vector128.Create(0u);
            Vector128<uint> yOffsets0 = Vector128.Create(0u, 1u, 2u, 3u);
            Vector128<float> denom = Vector128.Create(FloatMax);

            for (uint x = 0; x < uSize; x++)
            {
                Vector128<uint> v1 = Vector128.Create(unchecked(seed + posX + x));
                int rowBase = checked((int)(x * uSize));

                int y = 0;
                for (; y <= size - 4; y += 4)
                {
                    Vector128<uint> yVec = Sse2.Add(posYVec, Sse2.Add(yOffsets0, Vector128.Create((uint)y)));
                    Vector128<uint> hashed = HashU_Vector128_U32(v1, yVec, zeros);

                    Vector128<float> f = ConvertU32ToF32_Vector128(hashed);
                    Vector128<float> norm = Sse.Divide(f, denom);

                    Unsafe.WriteUnaligned(
                        ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, rowBase + y)),
                        norm);
                }

                for (; y < size; y++)
                {
                    uint u = HashU(unchecked(seed + posX + x), unchecked(posY + (uint)y), 0u);
                    buffer[rowBase + y] = u / FloatMax;
                }
            }

            return;
        }

        for (uint x = 0; x < uSize; x++)
        {
            int rowBase = checked((int)(x * uSize));
            for (uint y = 0; y < uSize; y++)
            {
                uint u = HashU(unchecked(seed + posX + x), unchecked(posY + y), 0u);
                buffer[rowBase + (int)y] = u / FloatMax;
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with deterministic Squirrel3 noise floats in [0, 1] for a 3D grid.
    /// </summary>
    public static void Noise01(uint seed, uint posX, uint posY, uint posZ, int size, Span<float> buffer)
    {
        int required = checked(size * size * size);
        if (buffer.Length < required)
        {
            throw new ArgumentException($"Buffer must be at least {required} elements long.", nameof(buffer));
        }

        if (size == 0)
        {
            return;
        }

        uint uSize = (uint)size;
        uint stride1 = uSize;
        uint stride2 = checked(uSize * uSize);

        if (Avx2.IsSupported)
        {
            ref float dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector256<uint> posZVec = Vector256.Create(posZ);
            Vector256<uint> zOffsets0 = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);
            Vector256<float> denom = Vector256.Create(FloatMax);

            for (uint x = 0; x < uSize; x++)
            {
                Vector256<uint> v1 = Vector256.Create(unchecked(seed + posX + x));

                for (uint y = 0; y < uSize; y++)
                {
                    Vector256<uint> v2 = Vector256.Create(unchecked(posY + y));

                    int sliceBase = checked((int)(x * stride2 + y * stride1));
                    int z = 0;
                    for (; z <= size - 8; z += 8)
                    {
                        Vector256<uint> zVec = Avx2.Add(posZVec, Avx2.Add(zOffsets0, Vector256.Create((uint)z)));
                        Vector256<uint> hashed = HashU_Vector256_U32(v1, v2, zVec);

                        Vector256<float> f = ConvertU32ToF32_Vector256(hashed);
                        Vector256<float> norm = Avx.Divide(f, denom);

                        Unsafe.WriteUnaligned(
                            ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, sliceBase + z)),
                            norm);
                    }

                    for (; z < size; z++)
                    {
                        uint u = HashU(unchecked(seed + posX + x), unchecked(posY + y), unchecked(posZ + (uint)z));
                        buffer[sliceBase + z] = u / FloatMax;
                    }
                }
            }

            return;
        }

        if (Sse2.IsSupported && Sse41.IsSupported)
        {
            ref float dstRef = ref MemoryMarshal.GetReference(buffer);
            Vector128<uint> posZVec = Vector128.Create(posZ);
            Vector128<uint> zOffsets0 = Vector128.Create(0u, 1u, 2u, 3u);
            Vector128<float> denom = Vector128.Create(FloatMax);

            for (uint x = 0; x < uSize; x++)
            {
                Vector128<uint> v1 = Vector128.Create(unchecked(seed + posX + x));

                for (uint y = 0; y < uSize; y++)
                {
                    Vector128<uint> v2 = Vector128.Create(unchecked(posY + y));

                    int sliceBase = checked((int)(x * stride2 + y * stride1));
                    int z = 0;
                    for (; z <= size - 4; z += 4)
                    {
                        Vector128<uint> zVec = Sse2.Add(posZVec, Sse2.Add(zOffsets0, Vector128.Create((uint)z)));
                        Vector128<uint> hashed = HashU_Vector128_U32(v1, v2, zVec);

                        Vector128<float> f = ConvertU32ToF32_Vector128(hashed);
                        Vector128<float> norm = Sse.Divide(f, denom);

                        Unsafe.WriteUnaligned(
                            ref Unsafe.As<float, byte>(ref Unsafe.Add(ref dstRef, sliceBase + z)),
                            norm);
                    }

                    for (; z < size; z++)
                    {
                        uint u = HashU(unchecked(seed + posX + x), unchecked(posY + y), unchecked(posZ + (uint)z));
                        buffer[sliceBase + z] = u / FloatMax;
                    }
                }
            }

            return;
        }

        for (uint x = 0; x < uSize; x++)
        {
            for (uint y = 0; y < uSize; y++)
            {
                int sliceBase = checked((int)(x * stride2 + y * stride1));
                for (uint z = 0; z < uSize; z++)
                {
                    uint u = HashU(unchecked(seed + posX + x), unchecked(posY + y), unchecked(posZ + z));
                    buffer[sliceBase + (int)z] = u / FloatMax;
                }
            }
        }
    }

    #endregion
}
