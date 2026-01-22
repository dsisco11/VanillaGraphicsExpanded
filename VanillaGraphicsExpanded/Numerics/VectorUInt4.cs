using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Numerics;

/// <summary>
/// 4D unsigned integer vector with optional SIMD-accelerated operators.
/// Stores the vector in a 128-bit lane as (X,Y,Z,W).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct VectorUInt4 : IEquatable<VectorUInt4>
{
    private readonly Vector128<uint> v;

    /// <summary>
    /// All-ones vector used for bitwise NOT (~x == x ^ AllOnes).
    /// </summary>
    private static Vector128<uint> AllOnes => Vector128.Create(uint.MaxValue);

    /// <summary>
    /// X component.
    /// </summary>
    public uint X => v.GetElement(0);

    /// <summary>
    /// Y component.
    /// </summary>
    public uint Y => v.GetElement(1);

    /// <summary>
    /// Z component.
    /// </summary>
    public uint Z => v.GetElement(2);

    /// <summary>
    /// W component.
    /// </summary>
    public uint W => v.GetElement(3);

    /// <summary>
    /// (0,0,0,0)
    /// </summary>
    public static VectorUInt4 Zero => default;

    /// <summary>
    /// (1,1,1,1)
    /// </summary>
    public static VectorUInt4 One => new(1, 1, 1, 1);

    /// <summary>
    /// (1,0,0,0)
    /// </summary>
    public static VectorUInt4 UnitX => new(1, 0, 0, 0);

    /// <summary>
    /// (0,1,0,0)
    /// </summary>
    public static VectorUInt4 UnitY => new(0, 1, 0, 0);

    /// <summary>
    /// (0,0,1,0)
    /// </summary>
    public static VectorUInt4 UnitZ => new(0, 0, 1, 0);

    /// <summary>
    /// (0,0,0,1)
    /// </summary>
    public static VectorUInt4 UnitW => new(0, 0, 0, 1);

    /// <summary>
    /// Creates a vector from components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorUInt4(uint x, uint y, uint z, uint w)
    {
        v = Vector128.Create(x, y, z, w);
    }

    /// <summary>
    /// Creates a vector with all components set to <paramref name="value"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorUInt4(uint value)
        : this(value, value, value, value)
    {
    }

    /// <summary>
    /// Converts to a <see cref="Vector4"/>.
    /// Note: values above 16,777,216 cannot be represented exactly in float.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector4 ToVector4() => new(X, Y, Z, W);

    /// <summary>
    /// Deconstructs into (x,y,z,w) components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out uint x, out uint y, out uint z, out uint w)
    {
        x = X;
        y = Y;
        z = Z;
        w = W;
    }

    /// <summary>
    /// Tests component-wise equality.
    /// Uses SIMD when supported.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(VectorUInt4 other)
    {
        return EqualsSimd(other);
    }

    /// <summary>
    /// Tests equality against an object.
    /// </summary>
    public override bool Equals(object? obj) => obj is VectorUInt4 other && Equals(other);

    /// <summary>
    /// Computes a hash code from the XYZW components.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <summary>
    /// Returns "(X, Y, Z, W)".
    /// </summary>
    public override string ToString() => $"({X}, {Y}, {Z}, {W})";

    /// <summary>
    /// Component-wise addition.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator +(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Add(a.v, b.v));
        }

        return new VectorUInt4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator +(VectorUInt4 a, uint b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Add(a.v, Vector128.Create(b)));
        }

        return new VectorUInt4(a.X + b, a.Y + b, a.Z + b, a.W + b);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator +(uint a, VectorUInt4 b) => b + a;

    /// <summary>
    /// Component-wise subtraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator -(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Subtract(a.v, b.v));
        }

        return new VectorUInt4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    }

    /// <summary>
    /// Subtracts a scalar from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator -(VectorUInt4 a, uint b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Subtract(a.v, Vector128.Create(b)));
        }

        return new VectorUInt4(a.X - b, a.Y - b, a.Z - b, a.W - b);
    }

    /// <summary>
    /// Computes (a - b) component-wise where <paramref name="a"/> is a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator -(uint a, VectorUInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Subtract(Vector128.Create(a), b.v));
        }

        return new VectorUInt4(a - b.X, a - b.Y, a - b.Z, a - b.W);
    }

    /// <summary>
    /// Negates each component (wraparound: 0 - x).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator -(VectorUInt4 a)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Subtract(Vector128<uint>.Zero, a.v));
        }

        return new VectorUInt4(0u - a.X, 0u - a.Y, 0u - a.Z, 0u - a.W);
    }

    /// <summary>
    /// Identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator +(VectorUInt4 a) => a;

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator *(VectorUInt4 a, uint scalar)
    {
        if (Sse41.IsSupported)
        {
            // MultiplyLow is defined for int lanes; low 32-bit product is identical for uint.
            Vector128<int> mul = Sse41.MultiplyLow(a.v.AsInt32(), Vector128.Create(unchecked((int)scalar)));
            return new VectorUInt4(mul.AsUInt32());
        }

        return new VectorUInt4(a.X * scalar, a.Y * scalar, a.Z * scalar, a.W * scalar);
    }

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator *(uint scalar, VectorUInt4 a) => a * scalar;

    /// <summary>
    /// Component-wise multiplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator *(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse41.IsSupported)
        {
            // MultiplyLow is defined for int lanes; low 32-bit product is identical for uint.
            Vector128<int> mul = Sse41.MultiplyLow(a.v.AsInt32(), b.v.AsInt32());
            return new VectorUInt4(mul.AsUInt32());
        }

        return new VectorUInt4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    }

    /// <summary>
    /// Divides each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator /(VectorUInt4 a, uint scalar)
    {
        // No integer SIMD division instruction; scalar fallback.
        return new VectorUInt4(a.X / scalar, a.Y / scalar, a.Z / scalar, a.W / scalar);
    }

    /// <summary>
    /// Component-wise integer division.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator /(VectorUInt4 a, VectorUInt4 b)
    {
        // No integer SIMD division instruction; scalar fallback.
        return new VectorUInt4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
    }

    /// <summary>
    /// Computes each component modulo a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator %(VectorUInt4 a, uint scalar)
    {
        // No integer SIMD modulo instruction; scalar fallback.
        return new VectorUInt4(a.X % scalar, a.Y % scalar, a.Z % scalar, a.W % scalar);
    }

    /// <summary>
    /// Component-wise modulo.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator %(VectorUInt4 a, VectorUInt4 b)
    {
        // No integer SIMD modulo instruction; scalar fallback.
        return new VectorUInt4(a.X % b.X, a.Y % b.Y, a.Z % b.Z, a.W % b.W);
    }

    /// <summary>
    /// Component-wise bitwise AND.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator &(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.And(a.v, b.v));
        }

        return new VectorUInt4(a.X & b.X, a.Y & b.Y, a.Z & b.Z, a.W & b.W);
    }

    /// <summary>
    /// Component-wise bitwise OR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator |(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Or(a.v, b.v));
        }

        return new VectorUInt4(a.X | b.X, a.Y | b.Y, a.Z | b.Z, a.W | b.W);
    }

    /// <summary>
    /// Component-wise bitwise XOR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator ^(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Xor(a.v, b.v));
        }

        return new VectorUInt4(a.X ^ b.X, a.Y ^ b.Y, a.Z ^ b.Z, a.W ^ b.W);
    }

    /// <summary>
    /// Component-wise bitwise NOT.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator ~(VectorUInt4 a)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt4(Sse2.Xor(a.v, AllOnes));
        }

        return new VectorUInt4(~a.X, ~a.Y, ~a.Z, ~a.W);
    }

    /// <summary>
    /// Shifts each component left by <paramref name="shift"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator <<(VectorUInt4 a, int shift)
    {
        if (Sse2.IsSupported)
        {
            // Variable-count shifts take the count from the low 64 bits of the vector.
            Vector128<int> count = Vector128.Create(shift, 0, 0, 0);
            Vector128<int> shifted = Sse2.ShiftLeftLogical(a.v.AsInt32(), count);
            return new VectorUInt4(shifted.AsUInt32());
        }

        return new VectorUInt4(a.X << shift, a.Y << shift, a.Z << shift, a.W << shift);
    }

    /// <summary>
    /// Shifts each component right (logical) by <paramref name="shift"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator >>(VectorUInt4 a, int shift)
    {
        if (Sse2.IsSupported)
        {
            // Variable-count shifts take the count from the low 64 bits of the vector.
            Vector128<int> count = Vector128.Create(shift, 0, 0, 0);
            Vector128<int> shifted = Sse2.ShiftRightLogical(a.v.AsInt32(), count);
            return new VectorUInt4(shifted.AsUInt32());
        }

        return new VectorUInt4(a.X >> shift, a.Y >> shift, a.Z >> shift, a.W >> shift);
    }

    /// <summary>
    /// Adds one to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator ++(VectorUInt4 a) => a + One;

    /// <summary>
    /// Subtracts one from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 operator --(VectorUInt4 a) => a - One;

    /// <summary>
    /// Equality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(VectorUInt4 left, VectorUInt4 right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(VectorUInt4 left, VectorUInt4 right) => !left.Equals(right);

    /// <summary>
    /// Component-wise minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 Min(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse41.IsSupported)
        {
            return new VectorUInt4(Sse41.Min(a.v, b.v));
        }

        return new VectorUInt4(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z), Math.Min(a.W, b.W));
    }

    /// <summary>
    /// Component-wise maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 Max(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse41.IsSupported)
        {
            return new VectorUInt4(Sse41.Max(a.v, b.v));
        }

        return new VectorUInt4(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z), Math.Max(a.W, b.W));
    }

    /// <summary>
    /// Clamps each component between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt4 Clamp(VectorUInt4 value, VectorUInt4 min, VectorUInt4 max)
    {
        return Min(Max(value, min), max);
    }

    /// <summary>
    /// Computes the 4-wide dot product (wraparound in uint arithmetic).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Dot(VectorUInt4 a, VectorUInt4 b)
    {
        if (Sse41.IsSupported)
        {
            Vector128<int> mul = Sse41.MultiplyLow(a.v.AsInt32(), b.v.AsInt32());
            uint s0 = unchecked((uint)mul.GetElement(0));
            uint s1 = unchecked((uint)mul.GetElement(1));
            uint s2 = unchecked((uint)mul.GetElement(2));
            uint s3 = unchecked((uint)mul.GetElement(3));
            return unchecked(s0 + s1 + s2 + s3);
        }

        return unchecked((a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W));
    }

    /// <summary>
    /// Computes the squared length (dot(v,v), wraparound in uint arithmetic).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LengthSquared(VectorUInt4 a) => Dot(a, a);

    /// <summary>
    /// Computes the squared length (dot(this,this), wraparound in uint arithmetic).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint LengthSquared() => LengthSquared(this);

    /// <summary>
    /// Creates a vector from a pre-packed <see cref="Vector128{UInt32}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private VectorUInt4(Vector128<uint> value)
    {
        v = value;
    }

    /// <summary>
    /// SIMD equality test.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EqualsSimd(VectorUInt4 other)
    {
        if (Sse2.IsSupported)
        {
            Vector128<uint> cmp = Sse2.CompareEqual(v, other.v);
            // All 16 bytes must be 0xFF for full equality.
            return Sse2.MoveMask(cmp.AsByte()) == 0xFFFF;
        }

        return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    }
}
