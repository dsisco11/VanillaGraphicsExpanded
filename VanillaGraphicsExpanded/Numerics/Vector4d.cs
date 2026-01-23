using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Numerics;

/// <summary>
/// 4D double-precision vector.
/// Uses AVX (<see cref="Vector256{T}"/>) to accelerate vector math when available.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vector4d : IEquatable<Vector4d>
{
    /// <summary>
    /// Packed as (X,Y,Z,W).
    /// </summary>
    private readonly Vector256<double> v;

    /// <summary>
    /// X component.
    /// </summary>
    public double X => v.GetElement(0);

    /// <summary>
    /// Y component.
    /// </summary>
    public double Y => v.GetElement(1);

    /// <summary>
    /// Z component.
    /// </summary>
    public double Z => v.GetElement(2);

    /// <summary>
    /// W component.
    /// </summary>
    public double W => v.GetElement(3);

    /// <summary>
    /// (0,0,0,0)
    /// </summary>
    public static Vector4d Zero => default;

    /// <summary>
    /// (1,1,1,1)
    /// </summary>
    public static Vector4d One => new(1d, 1d, 1d, 1d);

    /// <summary>
    /// (1,0,0,0)
    /// </summary>
    public static Vector4d UnitX => new(1d, 0d, 0d, 0d);

    /// <summary>
    /// (0,1,0,0)
    /// </summary>
    public static Vector4d UnitY => new(0d, 1d, 0d, 0d);

    /// <summary>
    /// (0,0,1,0)
    /// </summary>
    public static Vector4d UnitZ => new(0d, 0d, 1d, 0d);

    /// <summary>
    /// (0,0,0,1)
    /// </summary>
    public static Vector4d UnitW => new(0d, 0d, 0d, 1d);

    /// <summary>
    /// Creates a vector from components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector4d(double x, double y, double z, double w)
    {
        v = Vector256.Create(x, y, z, w);
    }

    /// <summary>
    /// Creates a vector with all components set to <paramref name="value"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector4d(double value)
        : this(value, value, value, value)
    {
    }

    /// <summary>
    /// Converts to <see cref="Vector4"/> (lossy for values outside float precision).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector4 ToVector4() => new((float)X, (float)Y, (float)Z, (float)W);

    /// <summary>
    /// Converts to <see cref="Vector4d"/> from a <see cref="Vector4"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d FromVector4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    /// <summary>
    /// Deconstructs into (x,y,z,w) components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out double x, out double y, out double z, out double w)
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
    public bool Equals(Vector4d other) => EqualsSimd(other);

    /// <summary>
    /// Tests equality against an object.
    /// </summary>
    public override bool Equals(object? obj) => obj is Vector4d other && Equals(other);

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
    public static Vector4d operator +(Vector4d a, Vector4d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Add(a.v, b.v));
        }

        return new Vector4d(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator +(Vector4d a, double b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Add(a.v, Vector256.Create(b)));
        }

        return new Vector4d(a.X + b, a.Y + b, a.Z + b, a.W + b);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator +(double a, Vector4d b) => b + a;

    /// <summary>
    /// Component-wise subtraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator -(Vector4d a, Vector4d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Subtract(a.v, b.v));
        }

        return new Vector4d(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    }

    /// <summary>
    /// Subtracts a scalar from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator -(Vector4d a, double b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Subtract(a.v, Vector256.Create(b)));
        }

        return new Vector4d(a.X - b, a.Y - b, a.Z - b, a.W - b);
    }

    /// <summary>
    /// Computes (a - b) component-wise where <paramref name="a"/> is a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator -(double a, Vector4d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Subtract(Vector256.Create(a), b.v));
        }

        return new Vector4d(a - b.X, a - b.Y, a - b.Z, a - b.W);
    }

    /// <summary>
    /// Negates each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator -(Vector4d a)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Subtract(Vector256<double>.Zero, a.v));
        }

        return new Vector4d(-a.X, -a.Y, -a.Z, -a.W);
    }

    /// <summary>
    /// Identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator +(Vector4d a) => a;

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator *(Vector4d a, double scalar)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Multiply(a.v, Vector256.Create(scalar)));
        }

        return new Vector4d(a.X * scalar, a.Y * scalar, a.Z * scalar, a.W * scalar);
    }

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator *(double scalar, Vector4d a) => a * scalar;

    /// <summary>
    /// Component-wise multiplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator *(Vector4d a, Vector4d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Multiply(a.v, b.v));
        }

        return new Vector4d(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    }

    /// <summary>
    /// Divides each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator /(Vector4d a, double scalar)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Divide(a.v, Vector256.Create(scalar)));
        }

        return new Vector4d(a.X / scalar, a.Y / scalar, a.Z / scalar, a.W / scalar);
    }

    /// <summary>
    /// Component-wise division.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d operator /(Vector4d a, Vector4d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Divide(a.v, b.v));
        }

        return new Vector4d(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
    }

    /// <summary>
    /// Component-wise minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d Min(Vector4d a, Vector4d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Min(a.v, b.v));
        }

        return new Vector4d(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z), Math.Min(a.W, b.W));
    }

    /// <summary>
    /// Component-wise maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d Max(Vector4d a, Vector4d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector4d(Avx.Max(a.v, b.v));
        }

        return new Vector4d(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z), Math.Max(a.W, b.W));
    }

    /// <summary>
    /// Clamps each component between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d Clamp(Vector4d value, Vector4d min, Vector4d max) => Min(Max(value, min), max);

    /// <summary>
    /// Computes the dot product.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(Vector4d a, Vector4d b)
    {
        if (Avx.IsSupported)
        {
            Vector256<double> mul = Avx.Multiply(a.v, b.v);
            Vector256<double> hadd = Avx.HorizontalAdd(mul, mul);
            // For (mul,mul), HorizontalAdd produces [s01, s01, s23, s23].
            return hadd.GetElement(0) + hadd.GetElement(2);
        }

        return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W);
    }

    /// <summary>
    /// Computes the squared length (dot(v,v)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double LengthSquared(Vector4d a) => Dot(a, a);

    /// <summary>
    /// Computes the squared length (dot(this,this)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double LengthSquared() => LengthSquared(this);

    /// <summary>
    /// Computes the Euclidean length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Length(Vector4d a) => Math.Sqrt(LengthSquared(a));

    /// <summary>
    /// Computes the Euclidean length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Length() => Length(this);

    /// <summary>
    /// Returns a normalized vector (or <see cref="Zero"/> if the length is too small).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4d Normalize(Vector4d v)
    {
        double len = Length(v);
        if (len <= 0d || double.IsNaN(len) || double.IsInfinity(len))
        {
            return Zero;
        }

        return v / len;
    }

    /// <summary>
    /// Returns a normalized copy of this vector (or <see cref="Zero"/> if the length is too small).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector4d Normalize() => Normalize(this);

    /// <summary>
    /// Computes the distance between two vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(Vector4d a, Vector4d b) => Length(a - b);

    /// <summary>
    /// Computes the squared distance between two vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DistanceSquared(Vector4d a, Vector4d b) => LengthSquared(a - b);

    /// <summary>
    /// Equality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector4d left, Vector4d right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector4d left, Vector4d right) => !left.Equals(right);

    /// <summary>
    /// SIMD equality test.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EqualsSimd(Vector4d other)
    {
        if (Avx.IsSupported)
        {
            Vector256<double> cmp = Avx.Compare(v, other.v, FloatComparisonMode.OrderedEqualNonSignaling);
            return Avx.MoveMask(cmp) == 0b1111;
        }

        return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector4d(Vector256<double> packed)
    {
        v = packed;
    }
}
