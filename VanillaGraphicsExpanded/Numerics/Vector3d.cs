using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Numerics;

/// <summary>
/// 3D double-precision vector.
/// Uses AVX (<see cref="Vector256{T}"/>) to accelerate vector math when available.
/// </summary>
internal readonly struct Vector3d : IEquatable<Vector3d>
{
    /// <summary>
    /// Packed as (X,Y,Z,0).
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
    /// (0,0,0)
    /// </summary>
    public static Vector3d Zero => default;

    /// <summary>
    /// (1,1,1)
    /// </summary>
    public static Vector3d One => new(1d, 1d, 1d);

    /// <summary>
    /// (1,0,0)
    /// </summary>
    public static Vector3d UnitX => new(1d, 0d, 0d);

    /// <summary>
    /// (0,1,0)
    /// </summary>
    public static Vector3d UnitY => new(0d, 1d, 0d);

    /// <summary>
    /// (0,0,1)
    /// </summary>
    public static Vector3d UnitZ => new(0d, 0d, 1d);

    /// <summary>
    /// Creates a vector from components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d(double x, double y, double z)
    {
        v = Vector256.Create(x, y, z, 0d);
    }

    /// <summary>
    /// Creates a vector with all components set to <paramref name="value"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d(double value)
        : this(value, value, value)
    {
    }

    /// <summary>
    /// Converts to <see cref="Vector3"/> (lossy for values outside float precision).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

    /// <summary>
    /// Converts to <see cref="Vector3d"/> from a <see cref="Vector3"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d FromVector3(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Deconstructs into (x,y,z) components.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out double x, out double y, out double z)
    {
        x = X;
        y = Y;
        z = Z;
    }

    /// <summary>
    /// Tests component-wise equality.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Vector3d other) => EqualsSimd(other);

    /// <summary>
    /// Tests equality against an object.
    /// </summary>
    public override bool Equals(object? obj) => obj is Vector3d other && Equals(other);

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
    public static Vector3d operator +(Vector3d a, Vector3d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Add(a.v, b.v)));
        }

        return new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator +(Vector3d a, double b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Add(a.v, Vector256.Create(b))));
        }

        return new Vector3d(a.X + b, a.Y + b, a.Z + b);
    }

    /// <summary>
    /// Adds a scalar to each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator +(double a, Vector3d b) => b + a;

    /// <summary>
    /// Component-wise subtraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator -(Vector3d a, Vector3d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Subtract(a.v, b.v)));
        }

        return new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    /// <summary>
    /// Subtracts a scalar from each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator -(Vector3d a, double b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Subtract(a.v, Vector256.Create(b))));
        }

        return new Vector3d(a.X - b, a.Y - b, a.Z - b);
    }

    /// <summary>
    /// Computes (a - b) component-wise where <paramref name="a"/> is a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator -(double a, Vector3d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Subtract(Vector256.Create(a), b.v)));
        }

        return new Vector3d(a - b.X, a - b.Y, a - b.Z);
    }

    /// <summary>
    /// Negates each component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator -(Vector3d a)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Subtract(Vector256<double>.Zero, a.v)));
        }

        return new Vector3d(-a.X, -a.Y, -a.Z);
    }

    /// <summary>
    /// Identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator +(Vector3d a) => a;

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator *(Vector3d a, double scalar)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Multiply(a.v, Vector256.Create(scalar))));
        }

        return new Vector3d(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    /// <summary>
    /// Multiplies each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator *(double scalar, Vector3d a) => a * scalar;

    /// <summary>
    /// Component-wise multiplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator *(Vector3d a, Vector3d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Multiply(a.v, b.v)));
        }

        return new Vector3d(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    }

    /// <summary>
    /// Divides each component by a scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator /(Vector3d a, double scalar)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Divide(a.v, Vector256.Create(scalar))));
        }

        return new Vector3d(a.X / scalar, a.Y / scalar, a.Z / scalar);
    }

    /// <summary>
    /// Component-wise division.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d operator /(Vector3d a, Vector3d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Divide(a.v, b.v)));
        }

        return new Vector3d(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    }

    /// <summary>
    /// Component-wise minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d Min(Vector3d a, Vector3d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Min(a.v, b.v)));
        }

        return new Vector3d(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    }

    /// <summary>
    /// Component-wise maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d Max(Vector3d a, Vector3d b)
    {
        if (Avx.IsSupported)
        {
            return new Vector3d(ZeroW(Avx.Max(a.v, b.v)));
        }

        return new Vector3d(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    }

    /// <summary>
    /// Component-wise absolute value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d Abs(Vector3d value)
    {
        if (Avx.IsSupported)
        {
            // abs(x) = x & ~signBit
            // For doubles, the sign bit mask corresponds to -0.0.
            Vector256<double> signMask = Vector256.Create(-0.0);
            Vector256<double> abs = Avx.AndNot(signMask, value.v);
            return new Vector3d(ZeroW(abs));
        }

        return new Vector3d(Math.Abs(value.X), Math.Abs(value.Y), Math.Abs(value.Z));
    }

    /// <summary>
    /// Component-wise floor.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d Floor(Vector3d value)
    {
        if (Avx.IsSupported)
        {
            Vector256<double> floored = Avx.RoundToNegativeInfinity(value.v);
            return new Vector3d(ZeroW(floored));
        }

        return new Vector3d(Math.Floor(value.X), Math.Floor(value.Y), Math.Floor(value.Z));
    }

    /// <summary>
    /// Floors each component and converts to a <see cref="VectorInt3"/>.
    /// Uses SIMD when available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorInt3 FloorToInt3(Vector3d value)
    {
        if (Avx.IsSupported)
        {
            // Guard against undefined conversion results for non-finite or out-of-range values.
            // In normal world coordinates we expect these to be in-range.
            double x = value.X;
            double y = value.Y;
            double z = value.Z;

            if (double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z)
                && x >= int.MinValue && x <= int.MaxValue
                && y >= int.MinValue && y <= int.MaxValue
                && z >= int.MinValue && z <= int.MaxValue)
            {
                Vector256<double> floored = Avx.RoundToNegativeInfinity(value.v);
                // Convert 4 doubles to 4 int32s with truncation. Since we pre-floor, truncation is correct.
                Vector128<int> ints = Avx.ConvertToVector128Int32WithTruncation(floored);
                return new VectorInt3(ints.GetElement(0), ints.GetElement(1), ints.GetElement(2));
            }
        }

        return new VectorInt3(
            (int)Math.Floor(value.X),
            (int)Math.Floor(value.Y),
            (int)Math.Floor(value.Z));
    }

    /// <summary>
    /// Clamps each component between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d Clamp(Vector3d value, Vector3d min, Vector3d max) => Min(Max(value, min), max);

    /// <summary>
    /// Computes the dot product.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(Vector3d a, Vector3d b)
    {
        if (Avx.IsSupported)
        {
            Vector256<double> mul = Avx.Multiply(a.v, b.v);
            // HorizontalAdd sums adjacent pairs within each 128-bit lane.
            // With W=0, sum of all lanes equals dot(XYZ).
            Vector256<double> hadd = Avx.HorizontalAdd(mul, mul);
            // For (mul,mul), HorizontalAdd produces [s01, s01, s23, s23].
            return hadd.GetElement(0) + hadd.GetElement(2);
        }

        return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
    }

    /// <summary>
    /// Computes the squared length (dot(v,v)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double LengthSquared(Vector3d a) => Dot(a, a);

    /// <summary>
    /// Computes the squared length (dot(this,this)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double LengthSquared() => LengthSquared(this);

    /// <summary>
    /// Computes the Euclidean length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Length(Vector3d a) => Math.Sqrt(LengthSquared(a));

    /// <summary>
    /// Computes the Euclidean length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Length() => Length(this);

    /// <summary>
    /// Returns a normalized vector (or <see cref="Zero"/> if the length is too small).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d Normalize(Vector3d v)
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
    public Vector3d Normalize() => Normalize(this);

    /// <summary>
    /// Computes the distance between two vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(Vector3d a, Vector3d b) => Length(a - b);

    /// <summary>
    /// Computes the squared distance between two vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DistanceSquared(Vector3d a, Vector3d b) => LengthSquared(a - b);

    /// <summary>
    /// Equality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector3d left, Vector3d right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector3d left, Vector3d right) => !left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> ZeroW(Vector256<double> value)
    {
        if (!Avx.IsSupported)
        {
            return value;
        }

        // Blend selects from the 2nd argument for lanes where the mask bit is 1.
        // Mask 0b1000 => replace W lane with 0.
        return Avx.Blend(value, Vector256<double>.Zero, 0b1000);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3d(Vector256<double> packed)
    {
        v = Avx.IsSupported ? ZeroW(packed) : Vector256.Create(packed.GetElement(0), packed.GetElement(1), packed.GetElement(2), 0d);
    }

    /// <summary>
    /// SIMD equality test.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EqualsSimd(Vector3d other)
    {
        if (Avx.IsSupported)
        {
            Vector256<double> cmp = Avx.Compare(v, other.v, FloatComparisonMode.OrderedEqualNonSignaling);
            return Avx.MoveMask(cmp) == 0b1111;
        }

        return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }

    /// <summary>
    /// Tests component-wise equality.
    /// Uses SIMD when supported.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EqualsAccelerated(Vector3d other) => EqualsSimd(other);
}
