using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Numerics;

/// <summary>
/// 3D integer vector with optional SIMD-accelerated operators.
/// Stores the vector in a 128-bit lane as (X,Y,Z,0).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct VectorInt3 : IEquatable<VectorInt3>
{
    private readonly Vector128<int> v;

    /// <summary>
    /// Lane mask for (X,Y,Z,0). Ensures the padding lane remains deterministically zero.
    /// </summary>
    private static Vector128<int> LaneMask => Vector128.Create(-1, -1, -1, 0);

    /// <summary>
    /// All-ones vector used for bitwise NOT (~x == x ^ AllOnes).
    /// </summary>
    private static Vector128<int> AllOnes => Vector128.Create(-1);

    /// <summary>
    /// X component.
    /// </summary>
    public int X => v.GetElement(0);

    /// <summary>
    /// Y component.
    /// </summary>
    public int Y => v.GetElement(1);

    /// <summary>
    /// Z component.
    /// </summary>
    public int Z => v.GetElement(2);

    /// <summary>
    /// (0,0,0)
    /// </summary>
    public static VectorInt3 Zero => default;

    /// <summary>
    /// (1,1,1)
    /// </summary>
    public static VectorInt3 One => new(1, 1, 1);

    /// <summary>
    /// (1,0,0)
    /// </summary>
    public static VectorInt3 UnitX => new(1, 0, 0);

    /// <summary>
    /// (0,1,0)
    /// </summary>
    public static VectorInt3 UnitY => new(0, 1, 0);

    /// <summary>
    /// (0,0,1)
    /// </summary>
    public static VectorInt3 UnitZ => new(0, 0, 1);

    /// <summary>
    /// Creates a vector from components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorInt3(int x, int y, int z)
    {
        v = Vector128.Create(x, y, z, 0);
    }

    /// <summary>
    /// Creates a vector with all components set to <paramref name="value"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorInt3(int value)
        : this(value, value, value)
    {
    }

    /// <summary>
    /// Converts to a <see cref="Vector3"/> (lossless for the 24-bit mantissa range of float).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 ToVector3() => new(X, Y, Z);

    /// <summary>
    /// Deconstructs into (x,y,z) components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out int x, out int y, out int z)
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
    public bool Equals(VectorInt3 other)
    {
        return EqualsSimd(other);
    }

    /// <summary>
    /// Tests equality against an object.
    /// </summary>
    public override bool Equals(object? obj) => obj is VectorInt3 other && Equals(other);

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
    public static VectorInt3 operator +(VectorInt3 a, VectorInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.Add(a.v, b.v));
        }

        return new VectorInt3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator +(VectorInt3 a, int b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.Add(a.v, Vector128.Create(b)));
        }

        return new VectorInt3(a.X + b, a.Y + b, a.Z + b);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator +(int a, VectorInt3 b) => b + a;

    /// <summary>
    /// Component-wise subtraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator -(VectorInt3 a, VectorInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.Subtract(a.v, b.v));
        }

        return new VectorInt3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    /// <summary>
    /// Subtracts a scalar from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator -(VectorInt3 a, int b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.Subtract(a.v, Vector128.Create(b)));
        }

        return new VectorInt3(a.X - b, a.Y - b, a.Z - b);
    }

    /// <summary>
    /// Computes (a - b) component-wise where <paramref name="a"/> is a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator -(int a, VectorInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.Subtract(Vector128.Create(a), b.v));
        }

        return new VectorInt3(a - b.X, a - b.Y, a - b.Z);
    }

    /// <summary>
    /// Negates each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator -(VectorInt3 a)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.Subtract(Vector128<int>.Zero, a.v));
        }

        return new VectorInt3(-a.X, -a.Y, -a.Z);
    }

    /// <summary>
    /// Identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator +(VectorInt3 a) => a;

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator *(VectorInt3 a, int scalar)
    {
        if (Sse41.IsSupported)
        {
            // MultiplyLow multiplies 32-bit lanes and returns the low 32 bits.
            // This matches normal int overflow semantics.
            Vector128<int> s = Vector128.Create(scalar);
            Vector128<int> mul = Sse41.MultiplyLow(a.v, s);
            // Ensure W lane stays 0.
            mul = Sse2.IsSupported ? Sse2.And(mul, LaneMask) : Vector128.Create(mul.GetElement(0), mul.GetElement(1), mul.GetElement(2), 0);
            return new VectorInt3(mul);
        }

        return new VectorInt3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator *(int scalar, VectorInt3 a) => a * scalar;

    /// <summary>
    /// Component-wise multiplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator *(VectorInt3 a, VectorInt3 b)
    {
        if (Sse41.IsSupported)
        {
            return new VectorInt3(Sse41.MultiplyLow(a.v, b.v));
        }

        return new VectorInt3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    }

    /// <summary>
    /// Divides each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator /(VectorInt3 a, int scalar)
    {
        // No integer SIMD division instruction; scalar fallback.
        return new VectorInt3(a.X / scalar, a.Y / scalar, a.Z / scalar);
    }

    /// <summary>
    /// Component-wise integer division.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator /(VectorInt3 a, VectorInt3 b)
    {
        // No integer SIMD division instruction; scalar fallback.
        return new VectorInt3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    }

    /// <summary>
    /// Computes each component modulo a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator %(VectorInt3 a, int scalar)
    {
        // No integer SIMD modulo instruction; scalar fallback.
        return new VectorInt3(a.X % scalar, a.Y % scalar, a.Z % scalar);
    }

    /// <summary>
    /// Component-wise modulo.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator %(VectorInt3 a, VectorInt3 b)
    {
        // No integer SIMD modulo instruction; scalar fallback.
        return new VectorInt3(a.X % b.X, a.Y % b.Y, a.Z % b.Z);
    }

    /// <summary>
    /// Component-wise bitwise AND.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator &(VectorInt3 a, VectorInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.And(a.v, b.v));
        }

        return new VectorInt3(a.X & b.X, a.Y & b.Y, a.Z & b.Z);
    }

    /// <summary>
    /// Component-wise bitwise OR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator |(VectorInt3 a, VectorInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.Or(a.v, b.v));
        }

        return new VectorInt3(a.X | b.X, a.Y | b.Y, a.Z | b.Z);
    }

    /// <summary>
    /// Component-wise bitwise XOR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator ^(VectorInt3 a, VectorInt3 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt3(Sse2.Xor(a.v, b.v));
        }

        return new VectorInt3(a.X ^ b.X, a.Y ^ b.Y, a.Z ^ b.Z);
    }

    /// <summary>
    /// Component-wise bitwise NOT.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator ~(VectorInt3 a)
    {
        if (Sse2.IsSupported)
        {
            // ~x == x ^ 0xFFFFFFFF
            return new VectorInt3(Sse2.Xor(a.v, AllOnes));
        }

        return new VectorInt3(~a.X, ~a.Y, ~a.Z);
    }

    /// <summary>
    /// Shifts each component left by <paramref name="shift"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator <<(VectorInt3 a, int shift)
    {
        if (Sse2.IsSupported)
        {
            // Variable-count shifts take the count from the low 64 bits of the vector.
            // Use (shift,0,0,0) so the low 64 bits equal shift.
            Vector128<int> count = Vector128.Create(shift, 0, 0, 0);
            return new VectorInt3(Sse2.ShiftLeftLogical(a.v, count));
        }

        return new VectorInt3(a.X << shift, a.Y << shift, a.Z << shift);
    }

    /// <summary>
    /// Shifts each component right (arithmetic) by <paramref name="shift"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator >>(VectorInt3 a, int shift)
    {
        if (Sse2.IsSupported)
        {
            // Variable-count shifts take the count from the low 64 bits of the vector.
            // Use (shift,0,0,0) so the low 64 bits equal shift.
            Vector128<int> count = Vector128.Create(shift, 0, 0, 0);
            return new VectorInt3(Sse2.ShiftRightArithmetic(a.v, count));
        }

        return new VectorInt3(a.X >> shift, a.Y >> shift, a.Z >> shift);
    }

    /// <summary>
    /// Adds one to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator ++(VectorInt3 a) => a + One;

    /// <summary>
    /// Subtracts one from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 operator --(VectorInt3 a) => a - One;

    /// <summary>
    /// Equality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(VectorInt3 left, VectorInt3 right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(VectorInt3 left, VectorInt3 right) => !left.Equals(right);

    /// <summary>
    /// Component-wise minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 Min(VectorInt3 a, VectorInt3 b)
    {
        if (Sse41.IsSupported)
        {
            return new VectorInt3(Sse41.Min(a.v, b.v));
        }

        return new VectorInt3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    }

    /// <summary>
    /// Component-wise maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 Max(VectorInt3 a, VectorInt3 b)
    {
        if (Sse41.IsSupported)
        {
            return new VectorInt3(Sse41.Max(a.v, b.v));
        }

        return new VectorInt3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    }

    /// <summary>
    /// Clamps each component between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 Clamp(VectorInt3 value, VectorInt3 min, VectorInt3 max)
    {
        return Min(Max(value, min), max);
    }

    /// <summary>
    /// Computes the 3-wide dot product.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Dot(VectorInt3 a, VectorInt3 b)
    {
        if (Sse41.IsSupported)
        {
            // Vector multiply, then horizontal sum (scalar reduction).
            // Keep W lane at 0 so it doesn't affect the sum.
            Vector128<int> mul = Sse41.MultiplyLow(a.v, b.v);
            mul = Sse2.IsSupported ? Sse2.And(mul, LaneMask) : Vector128.Create(mul.GetElement(0), mul.GetElement(1), mul.GetElement(2), 0);
            return mul.GetElement(0) + mul.GetElement(1) + mul.GetElement(2);
        }

        return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
    }

    /// <summary>
    /// Computes the squared length (dot(v,v)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LengthSquared(VectorInt3 a) => Dot(a, a);

    /// <summary>
    /// Computes the squared length (dot(this,this)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int LengthSquared() => LengthSquared(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private VectorInt3(Vector128<int> value)
    {
        // Always keep W lane deterministically zero.
        v = Sse2.IsSupported ? Sse2.And(value, LaneMask) : Vector128.Create(value.GetElement(0), value.GetElement(1), value.GetElement(2), 0);

        // Ensure padding lane is zeroed (debug-only correctness; keeps behavior deterministic).
        Debug.Assert(v.GetElement(3) == 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EqualsSimd(VectorInt3 other)
    {
        if (Sse2.IsSupported)
        {
            Vector128<int> cmp = Sse2.CompareEqual(v, other.v);
            // All 16 bytes must be 0xFF for full equality.
            return Sse2.MoveMask(cmp.AsByte()) == 0xFFFF;
        }

        return Equals(other);
    }
}
