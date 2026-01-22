using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Numerics;

/// <summary>
/// 4D integer vector with optional SIMD-accelerated operators.
/// Stores the vector in a 128-bit lane as (X,Y,Z,W).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct VectorInt4 : IEquatable<VectorInt4>
{
    private readonly Vector128<int> v;

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
    /// W component.
    /// </summary>
    public int W => v.GetElement(3);

    /// <summary>
    /// (0,0,0,0)
    /// </summary>
    public static VectorInt4 Zero => default;

    /// <summary>
    /// (1,1,1,1)
    /// </summary>
    public static VectorInt4 One => new(1, 1, 1, 1);

    /// <summary>
    /// (1,0,0,0)
    /// </summary>
    public static VectorInt4 UnitX => new(1, 0, 0, 0);

    /// <summary>
    /// (0,1,0,0)
    /// </summary>
    public static VectorInt4 UnitY => new(0, 1, 0, 0);

    /// <summary>
    /// (0,0,1,0)
    /// </summary>
    public static VectorInt4 UnitZ => new(0, 0, 1, 0);

    /// <summary>
    /// (0,0,0,1)
    /// </summary>
    public static VectorInt4 UnitW => new(0, 0, 0, 1);

    /// <summary>
    /// Creates a vector from components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorInt4(int x, int y, int z, int w)
    {
        v = Vector128.Create(x, y, z, w);
    }

    /// <summary>
    /// Creates a vector with all components set to <paramref name="value"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorInt4(int value)
        : this(value, value, value, value)
    {
    }

    /// <summary>
    /// Converts to a <see cref="Vector4"/> (lossless for the 24-bit mantissa range of float).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector4 ToVector4() => new(X, Y, Z, W);

    /// <summary>
    /// Deconstructs into (x,y,z,w) components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out int x, out int y, out int z, out int w)
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
    public bool Equals(VectorInt4 other)
    {
        return EqualsSimd(other);
    }

    /// <summary>
    /// Tests equality against an object.
    /// </summary>
    public override bool Equals(object? obj) => obj is VectorInt4 other && Equals(other);

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
    public static VectorInt4 operator +(VectorInt4 a, VectorInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Add(a.v, b.v));
        }

        return new VectorInt4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator +(VectorInt4 a, int b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Add(a.v, Vector128.Create(b)));
        }

        return new VectorInt4(a.X + b, a.Y + b, a.Z + b, a.W + b);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator +(int a, VectorInt4 b) => b + a;

    /// <summary>
    /// Component-wise subtraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator -(VectorInt4 a, VectorInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Subtract(a.v, b.v));
        }

        return new VectorInt4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    }

    /// <summary>
    /// Subtracts a scalar from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator -(VectorInt4 a, int b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Subtract(a.v, Vector128.Create(b)));
        }

        return new VectorInt4(a.X - b, a.Y - b, a.Z - b, a.W - b);
    }

    /// <summary>
    /// Computes (a - b) component-wise where <paramref name="a"/> is a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator -(int a, VectorInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Subtract(Vector128.Create(a), b.v));
        }

        return new VectorInt4(a - b.X, a - b.Y, a - b.Z, a - b.W);
    }

    /// <summary>
    /// Negates each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator -(VectorInt4 a)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Subtract(Vector128<int>.Zero, a.v));
        }

        return new VectorInt4(-a.X, -a.Y, -a.Z, -a.W);
    }

    /// <summary>
    /// Identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator +(VectorInt4 a) => a;

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator *(VectorInt4 a, int scalar)
    {
        if (Sse41.IsSupported)
        {
            Vector128<int> s = Vector128.Create(scalar);
            return new VectorInt4(Sse41.MultiplyLow(a.v, s));
        }

        return new VectorInt4(a.X * scalar, a.Y * scalar, a.Z * scalar, a.W * scalar);
    }

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator *(int scalar, VectorInt4 a) => a * scalar;

    /// <summary>
    /// Component-wise multiplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator *(VectorInt4 a, VectorInt4 b)
    {
        if (Sse41.IsSupported)
        {
            return new VectorInt4(Sse41.MultiplyLow(a.v, b.v));
        }

        return new VectorInt4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    }

    /// <summary>
    /// Divides each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator /(VectorInt4 a, int scalar)
    {
        // No integer SIMD division instruction; scalar fallback.
        return new VectorInt4(a.X / scalar, a.Y / scalar, a.Z / scalar, a.W / scalar);
    }

    /// <summary>
    /// Component-wise integer division.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator /(VectorInt4 a, VectorInt4 b)
    {
        // No integer SIMD division instruction; scalar fallback.
        return new VectorInt4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
    }

    /// <summary>
    /// Computes each component modulo a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator %(VectorInt4 a, int scalar)
    {
        // No integer SIMD modulo instruction; scalar fallback.
        return new VectorInt4(a.X % scalar, a.Y % scalar, a.Z % scalar, a.W % scalar);
    }

    /// <summary>
    /// Component-wise modulo.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator %(VectorInt4 a, VectorInt4 b)
    {
        // No integer SIMD modulo instruction; scalar fallback.
        return new VectorInt4(a.X % b.X, a.Y % b.Y, a.Z % b.Z, a.W % b.W);
    }

    /// <summary>
    /// Component-wise bitwise AND.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator &(VectorInt4 a, VectorInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.And(a.v, b.v));
        }

        return new VectorInt4(a.X & b.X, a.Y & b.Y, a.Z & b.Z, a.W & b.W);
    }

    /// <summary>
    /// Component-wise bitwise OR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator |(VectorInt4 a, VectorInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Or(a.v, b.v));
        }

        return new VectorInt4(a.X | b.X, a.Y | b.Y, a.Z | b.Z, a.W | b.W);
    }

    /// <summary>
    /// Component-wise bitwise XOR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator ^(VectorInt4 a, VectorInt4 b)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Xor(a.v, b.v));
        }

        return new VectorInt4(a.X ^ b.X, a.Y ^ b.Y, a.Z ^ b.Z, a.W ^ b.W);
    }

    /// <summary>
    /// Component-wise bitwise NOT.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator ~(VectorInt4 a)
    {
        if (Sse2.IsSupported)
        {
            return new VectorInt4(Sse2.Xor(a.v, AllOnes));
        }

        return new VectorInt4(~a.X, ~a.Y, ~a.Z, ~a.W);
    }

    /// <summary>
    /// Shifts each component left by <paramref name="shift"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator <<(VectorInt4 a, int shift)
    {
        if (Sse2.IsSupported)
        {
            // Variable-count shifts take the count from the low 64 bits of the vector.
            Vector128<int> count = Vector128.Create(shift, 0, 0, 0);
            return new VectorInt4(Sse2.ShiftLeftLogical(a.v, count));
        }

        return new VectorInt4(a.X << shift, a.Y << shift, a.Z << shift, a.W << shift);
    }

    /// <summary>
    /// Shifts each component right (arithmetic) by <paramref name="shift"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator >>(VectorInt4 a, int shift)
    {
        if (Sse2.IsSupported)
        {
            // Variable-count shifts take the count from the low 64 bits of the vector.
            Vector128<int> count = Vector128.Create(shift, 0, 0, 0);
            return new VectorInt4(Sse2.ShiftRightArithmetic(a.v, count));
        }

        return new VectorInt4(a.X >> shift, a.Y >> shift, a.Z >> shift, a.W >> shift);
    }

    /// <summary>
    /// Adds one to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator ++(VectorInt4 a) => a + One;

    /// <summary>
    /// Subtracts one from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 operator --(VectorInt4 a) => a - One;

    /// <summary>
    /// Equality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(VectorInt4 left, VectorInt4 right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(VectorInt4 left, VectorInt4 right) => !left.Equals(right);

    /// <summary>
    /// Component-wise minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 Min(VectorInt4 a, VectorInt4 b)
    {
        if (Sse41.IsSupported)
        {
            return new VectorInt4(Sse41.Min(a.v, b.v));
        }

        return new VectorInt4(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z), Math.Min(a.W, b.W));
    }

    /// <summary>
    /// Component-wise maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 Max(VectorInt4 a, VectorInt4 b)
    {
        if (Sse41.IsSupported)
        {
            return new VectorInt4(Sse41.Max(a.v, b.v));
        }

        return new VectorInt4(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z), Math.Max(a.W, b.W));
    }

    /// <summary>
    /// Clamps each component between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt4 Clamp(VectorInt4 value, VectorInt4 min, VectorInt4 max)
    {
        return Min(Max(value, min), max);
    }

    /// <summary>
    /// Computes the 4-wide dot product.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Dot(VectorInt4 a, VectorInt4 b)
    {
        if (Sse41.IsSupported)
        {
            Vector128<int> mul = Sse41.MultiplyLow(a.v, b.v);
            return mul.GetElement(0) + mul.GetElement(1) + mul.GetElement(2) + mul.GetElement(3);
        }

        return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W);
    }

    /// <summary>
    /// Computes the squared length (dot(v,v)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LengthSquared(VectorInt4 a) => Dot(a, a);

    /// <summary>
    /// Computes the squared length (dot(this,this)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int LengthSquared() => LengthSquared(this);

    /// <summary>
    /// Creates a vector from a pre-packed <see cref="Vector128{Int32}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private VectorInt4(Vector128<int> value)
    {
        v = value;
    }

    /// <summary>
    /// SIMD equality test.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EqualsSimd(VectorInt4 other)
    {
        if (Sse2.IsSupported)
        {
            Vector128<int> cmp = Sse2.CompareEqual(v, other.v);
            // All 16 bytes must be 0xFF for full equality.
            return Sse2.MoveMask(cmp.AsByte()) == 0xFFFF;
        }

        return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    }
}
