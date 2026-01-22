using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Numerics;

/// <summary>
/// 3D unsigned integer vector with optional SIMD-accelerated operators.
/// Stores the vector in a 128-bit lane as (X,Y,Z,0).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct VectorUInt3 : IEquatable<VectorUInt3>
{
    private readonly Vector128<uint> v;

    /// <summary>
    /// Lane mask for (X,Y,Z,0). Ensures the padding lane remains deterministically zero.
    /// </summary>
    private static Vector128<uint> LaneMask => Vector128.Create(uint.MaxValue, uint.MaxValue, uint.MaxValue, 0u);

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
    /// (0,0,0)
    /// </summary>
    public static VectorUInt3 Zero => default;

    /// <summary>
    /// (1,1,1)
    /// </summary>
    public static VectorUInt3 One => new(1, 1, 1);

    /// <summary>
    /// (1,0,0)
    /// </summary>
    public static VectorUInt3 UnitX => new(1, 0, 0);

    /// <summary>
    /// (0,1,0)
    /// </summary>
    public static VectorUInt3 UnitY => new(0, 1, 0);

    /// <summary>
    /// (0,0,1)
    /// </summary>
    public static VectorUInt3 UnitZ => new(0, 0, 1);

    /// <summary>
    /// Creates a vector from components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorUInt3(uint x, uint y, uint z)
    {
        v = Vector128.Create(x, y, z, 0u);
    }

    /// <summary>
    /// Creates a vector with all components set to <paramref name="value"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorUInt3(uint value)
        : this(value, value, value)
    {
    }

    /// <summary>
    /// Converts to a <see cref="Vector3"/>.
    /// Note: values above 16,777,216 cannot be represented exactly in float.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 ToVector3() => new(X, Y, Z);

    /// <summary>
    /// Deconstructs into (x,y,z) components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out uint x, out uint y, out uint z)
    {
        x = X;
        y = Y;
        z = Z;
    }

    /// <summary>
    /// Tests component-wise equality.
    /// Uses SIMD when supported.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(VectorUInt3 other)
    {
        return EqualsSimd(other);
    }

    /// <summary>
    /// Tests equality against an object.
    /// </summary>
    public override bool Equals(object? obj) => obj is VectorUInt3 other && Equals(other);

    /// <summary>
    /// Computes a hash code from the XYZ components.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>
    /// Returns "(X, Y, Z)".
    /// </summary>
    public override string ToString() => $"({X}, {Y}, {Z})";

    /// <summary>
    /// Component-wise addition.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator +(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.Add(a.v, b.v));
        }

        return new VectorUInt3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator +(VectorUInt3 a, uint b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.Add(a.v, Vector128.Create(b)));
        }

        return new VectorUInt3(a.X + b, a.Y + b, a.Z + b);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator +(uint a, VectorUInt3 b) => b + a;

    /// <summary>
    /// Component-wise subtraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator -(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.Subtract(a.v, b.v));
        }

        return new VectorUInt3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    /// <summary>
    /// Subtracts a scalar from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator -(VectorUInt3 a, uint b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.Subtract(a.v, Vector128.Create(b)));
        }

        return new VectorUInt3(a.X - b, a.Y - b, a.Z - b);
    }

    /// <summary>
    /// Computes (a - b) component-wise where <paramref name="a"/> is a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator -(uint a, VectorUInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.Subtract(Vector128.Create(a), b.v));
        }

        return new VectorUInt3(a - b.X, a - b.Y, a - b.Z);
    }

    /// <summary>
    /// Negates each component (wraparound: 0 - x).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator -(VectorUInt3 a)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.Subtract(Vector128<uint>.Zero, a.v));
        }

        return new VectorUInt3(0u - a.X, 0u - a.Y, 0u - a.Z);
    }

    /// <summary>
    /// Identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator +(VectorUInt3 a) => a;

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator *(VectorUInt3 a, uint scalar)
    {
        if (Sse41.IsSupported)
        {
            Vector128<int> mul = Sse41.MultiplyLow(a.v.AsInt32(), Vector128.Create(unchecked((int)scalar)));
            Vector128<uint> u = mul.AsUInt32();
            u = Sse2.IsSupported ? Sse2.And(u, LaneMask) : Vector128.Create(u.GetElement(0), u.GetElement(1), u.GetElement(2), 0u);
            return new VectorUInt3(u);
        }

        return new VectorUInt3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator *(uint scalar, VectorUInt3 a) => a * scalar;

    /// <summary>
    /// Component-wise multiplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator *(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse41.IsSupported)
        {
            Vector128<int> mul = Sse41.MultiplyLow(a.v.AsInt32(), b.v.AsInt32());
            Vector128<uint> u = mul.AsUInt32();
            u = Sse2.IsSupported ? Sse2.And(u, LaneMask) : Vector128.Create(u.GetElement(0), u.GetElement(1), u.GetElement(2), 0u);
            return new VectorUInt3(u);
        }

        return new VectorUInt3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    }

    /// <summary>
    /// Divides each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator /(VectorUInt3 a, uint scalar)
    {
        // No integer SIMD division instruction; scalar fallback.
        return new VectorUInt3(a.X / scalar, a.Y / scalar, a.Z / scalar);
    }

    /// <summary>
    /// Component-wise integer division.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator /(VectorUInt3 a, VectorUInt3 b)
    {
        // No integer SIMD division instruction; scalar fallback.
        return new VectorUInt3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    }

    /// <summary>
    /// Computes each component modulo a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator %(VectorUInt3 a, uint scalar)
    {
        // No integer SIMD modulo instruction; scalar fallback.
        return new VectorUInt3(a.X % scalar, a.Y % scalar, a.Z % scalar);
    }

    /// <summary>
    /// Component-wise modulo.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator %(VectorUInt3 a, VectorUInt3 b)
    {
        // No integer SIMD modulo instruction; scalar fallback.
        return new VectorUInt3(a.X % b.X, a.Y % b.Y, a.Z % b.Z);
    }

    /// <summary>
    /// Component-wise bitwise AND.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator &(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.And(a.v, b.v));
        }

        return new VectorUInt3(a.X & b.X, a.Y & b.Y, a.Z & b.Z);
    }

    /// <summary>
    /// Component-wise bitwise OR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator |(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.Or(a.v, b.v));
        }

        return new VectorUInt3(a.X | b.X, a.Y | b.Y, a.Z | b.Z);
    }

    /// <summary>
    /// Component-wise bitwise XOR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator ^(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorUInt3(Sse2.Xor(a.v, b.v));
        }

        return new VectorUInt3(a.X ^ b.X, a.Y ^ b.Y, a.Z ^ b.Z);
    }

    /// <summary>
    /// Component-wise bitwise NOT.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator ~(VectorUInt3 a)
    {
        if (Sse2.IsSupported)
        {
            Vector128<uint> inv = Sse2.Xor(a.v, AllOnes);
            inv = Sse2.And(inv, LaneMask);
            return new VectorUInt3(inv);
        }

        return new VectorUInt3(~a.X, ~a.Y, ~a.Z);
    }

    /// <summary>
    /// Shifts each component left by <paramref name="shift"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator <<(VectorUInt3 a, int shift)
    {
        if (Sse2.IsSupported)
        {
            Vector128<int> count = Vector128.Create(shift, 0, 0, 0);
            Vector128<int> shifted = Sse2.ShiftLeftLogical(a.v.AsInt32(), count);
            Vector128<uint> u = Sse2.And(shifted.AsUInt32(), LaneMask);
            return new VectorUInt3(u);
        }

        return new VectorUInt3(a.X << shift, a.Y << shift, a.Z << shift);
    }

    /// <summary>
    /// Shifts each component right (logical) by <paramref name="shift"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator >>(VectorUInt3 a, int shift)
    {
        if (Sse2.IsSupported)
        {
            Vector128<int> count = Vector128.Create(shift, 0, 0, 0);
            Vector128<int> shifted = Sse2.ShiftRightLogical(a.v.AsInt32(), count);
            Vector128<uint> u = Sse2.And(shifted.AsUInt32(), LaneMask);
            return new VectorUInt3(u);
        }

        return new VectorUInt3(a.X >> shift, a.Y >> shift, a.Z >> shift);
    }

    /// <summary>
    /// Adds one to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator ++(VectorUInt3 a) => a + One;

    /// <summary>
    /// Subtracts one from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 operator --(VectorUInt3 a) => a - One;

    /// <summary>
    /// Equality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(VectorUInt3 left, VectorUInt3 right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(VectorUInt3 left, VectorUInt3 right) => !left.Equals(right);

    /// <summary>
    /// Component-wise minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 Min(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse41.IsSupported)
        {
            Vector128<uint> m = Sse41.Min(a.v, b.v);
            m = Sse2.IsSupported ? Sse2.And(m, LaneMask) : Vector128.Create(m.GetElement(0), m.GetElement(1), m.GetElement(2), 0u);
            return new VectorUInt3(m);
        }

        return new VectorUInt3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    }

    /// <summary>
    /// Component-wise maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 Max(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse41.IsSupported)
        {
            Vector128<uint> m = Sse41.Max(a.v, b.v);
            m = Sse2.IsSupported ? Sse2.And(m, LaneMask) : Vector128.Create(m.GetElement(0), m.GetElement(1), m.GetElement(2), 0u);
            return new VectorUInt3(m);
        }

        return new VectorUInt3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    }

    /// <summary>
    /// Clamps each component between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorUInt3 Clamp(VectorUInt3 value, VectorUInt3 min, VectorUInt3 max)
    {
        return Min(Max(value, min), max);
    }

    /// <summary>
    /// Computes the 3-wide dot product (wraparound in uint arithmetic).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Dot(VectorUInt3 a, VectorUInt3 b)
    {
        if (Sse41.IsSupported)
        {
            Vector128<int> mul = Sse41.MultiplyLow(a.v.AsInt32(), b.v.AsInt32());
            uint s0 = unchecked((uint)mul.GetElement(0));
            uint s1 = unchecked((uint)mul.GetElement(1));
            uint s2 = unchecked((uint)mul.GetElement(2));
            return unchecked(s0 + s1 + s2);
        }

        return unchecked((a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z));
    }

    /// <summary>
    /// Computes the squared length (dot(v,v), wraparound in uint arithmetic).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LengthSquared(VectorUInt3 a) => Dot(a, a);

    /// <summary>
    /// Computes the squared length (dot(this,this), wraparound in uint arithmetic).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint LengthSquared() => LengthSquared(this);

    /// <summary>
    /// Creates a vector from a pre-packed <see cref="Vector128{UInt32}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private VectorUInt3(Vector128<uint> value)
    {
        v = Sse2.IsSupported ? Sse2.And(value, LaneMask) : Vector128.Create(value.GetElement(0), value.GetElement(1), value.GetElement(2), 0u);
    }

    /// <summary>
    /// SIMD equality test.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EqualsSimd(VectorUInt3 other)
    {
        if (Sse2.IsSupported)
        {
            Vector128<uint> cmp = Sse2.CompareEqual(v, other.v);
            return Sse2.MoveMask(cmp.AsByte()) == 0xFFFF;
        }

        return X == other.X && Y == other.Y && Z == other.Z;
    }
}
