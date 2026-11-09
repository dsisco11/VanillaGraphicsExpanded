using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit.Numerics;

/// <summary>Verifies the coordinate, conversion, arithmetic, and numeric contracts of <see cref="Vector4d"/>.</summary>
public sealed class Vector4dTests
{
    #region Public API
    #region Construction and conversion
    /// <summary>Creates a vector with the supplied coordinates and exposes them through its properties.</summary>
    [Fact]
    public void Ctor_And_Properties_Work()
    {
        // Arrange: use distinct coordinates so every property can be checked independently.
        // Act: construct the vector from the arranged coordinates.
        var v = new Vector4d(1.5, -2.25, 3.75, -4.5);
        // Assert: each property exposes its corresponding constructor argument.
        Assert.Equal(1.5, v.X);
        Assert.Equal(-2.25, v.Y);
        Assert.Equal(3.75, v.Z);
        Assert.Equal(-4.5, v.W);
    }

    /// <summary>Converts between the project vector and System.Numerics.Vector4 while preserving coordinates.</summary>
    [Fact]
    public void ToVector4_And_FromVector4_Work()
    {
        // Arrange: distinct coordinates make component swaps visible in both conversion directions.
        var d = new Vector4d(1.0, 2.0, 3.0, 4.0);
        // Act: convert the project vector to the framework vector.
        Vector4 f = d.ToVector4();
        // Assert: the framework value preserves each component.
        Assert.Equal(new Vector4(1, 2, 3, 4), f);

        // Act: convert a separate framework vector back to the project type.
        var d2 = Vector4d.FromVector4(new Vector4(4, 5, 6, 7));
        // Assert: reverse conversion preserves the distinct component values.
        Assert.Equal(new Vector4d(4, 5, 6, 7), d2);
    }

    /// <summary>Returns each coordinate through tuple deconstruction.</summary>
    [Fact]
    public void Deconstruct_Works()
    {
        // Arrange: assign distinct values so tuple coordinate order is observable.
        var v = new Vector4d(7, 8, 9, 10);
        // Act: deconstruct the vector into its coordinate variables.
        var (x, y, z, w) = v;
        // Assert: each tuple position matches the corresponding vector coordinate.
        Assert.Equal(7, x);
        Assert.Equal(8, y);
        Assert.Equal(9, z);
        Assert.Equal(10, w);
    }
    #endregion

    #region Equality and arithmetic
    /// <summary>Distinguishes equal and differing coordinate values with the equality operators.</summary>
    [Fact]
    public void Equality_Works()
    {
        // Arrange: prepare identical vectors and a second vector with one changed coordinate.
        var a = new Vector4d(1, 2, 3, 4);
        var b = new Vector4d(1, 2, 3, 4);
        var c = new Vector4d(1, 2, 3, 5);

        // Act and Assert: compare the equal and differing values through equality operators..
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));

        Assert.False(a == c);
        Assert.True(a != c);
    }

    /// <summary>Applies unary, scalar, vector, and componentwise arithmetic operators.</summary>
    [Fact]
    public void Operators_Work()
    {
        // Arrange: use distinct operands so scalar and componentwise arithmetic results differ.
        var a = new Vector4d(1, 2, 3, 4);
        var b = new Vector4d(10, 20, 30, 40);

        // Act and Assert: evaluate vector, scalar, unary, and componentwise operators inline..
        Assert.Equal(new Vector4d(11, 22, 33, 44), a + b);
        Assert.Equal(new Vector4d(-9, -18, -27, -36), a - b);

        Assert.Equal(new Vector4d(6, 7, 8, 9), a + 5);
        Assert.Equal(new Vector4d(6, 7, 8, 9), 5 + a);

        Assert.Equal(new Vector4d(-4, -3, -2, -1), a - 5);
        Assert.Equal(new Vector4d(4, 3, 2, 1), 5 - a);

        Assert.Equal(a, +a);
        Assert.Equal(new Vector4d(-1, -2, -3, -4), -a);

        Assert.Equal(new Vector4d(2, 4, 6, 8), a * 2);
        Assert.Equal(new Vector4d(2, 4, 6, 8), 2 * a);
        Assert.Equal(new Vector4d(10, 40, 90, 160), a * b);

        Assert.Equal(new Vector4d(0.5, 1, 1.5, 2), a / 2);
        Assert.Equal(new Vector4d(0.1, 0.1, 0.1, 0.1), a / new Vector4d(10, 20, 30, 40));
    }
    #endregion

    #region Bounds and geometry
    /// <summary>Selects componentwise minima and maxima and clamps coordinates to the supplied bounds.</summary>
    [Fact]
    public void Min_Max_Clamp_Work()
    {
        // Arrange: reverse coordinate ordering so Min and Max must select independently per axis.
        var a = new Vector4d(1, 10, -5, 3);
        var b = new Vector4d(2, -3, -6, 7);

        // Act and assert the independently expected coordinate extrema.
        Assert.Equal(new Vector4d(1, -3, -6, 3), Vector4d.Min(a, b));
        Assert.Equal(new Vector4d(2, 10, -5, 7), Vector4d.Max(a, b));

        // Arrange: cross lower and upper bounds and include an equal lower/upper bound on Z.
        var v = new Vector4d(5, -10, 3, 9);
        var lo = new Vector4d(0, -5, 2, 8);
        var hi = new Vector4d(4, 5, 2, 8);
        // Act: clamp the value against these bounds.
        // Assert: coordinates hit lower, upper, and shared bounds as arranged.
        Assert.Equal(new Vector4d(4, -5, 2, 8), Vector4d.Clamp(v, lo, hi));
    }

    /// <summary>Computes dot product, lengths, and distances from the vector coordinates.</summary>
    [Fact]
    public void Dot_Length_Distance_Work()
    {
        // Arrange: choose small coordinates for direct dot, length, and distance calculations.
        var a = new Vector4d(2, 3, 4, 5);
        var b = new Vector4d(5, 6, 7, 8);

        // Act and Assert: evaluate each static and instance measurement inline..
        Assert.Equal((2 * 5) + (3 * 6) + (4 * 7) + (5 * 8), Vector4d.Dot(a, b));
        Assert.Equal((2 * 2) + (3 * 3) + (4 * 4) + (5 * 5), Vector4d.LengthSquared(a));
        Assert.Equal(Vector4d.LengthSquared(a), a.LengthSquared());

        Assert.Equal(Math.Sqrt(Vector4d.LengthSquared(a)), Vector4d.Length(a), 12);
        Assert.Equal(Vector4d.Length(a), a.Length(), 12);

        Assert.Equal(Vector4d.Length(a - b), Vector4d.Distance(a, b), 12);
        Assert.Equal(Vector4d.LengthSquared(a - b), Vector4d.DistanceSquared(a, b), 12);
    }

    /// <summary>Normalizes a nonzero vector to unit length.</summary>
    [Fact]
    public void Normalize_Works_For_UnitAxis()
    {
        // Arrange: choose a non-unit vector aligned with one axis.
        var a = new Vector4d(0, 3, 0, 0);
        // Act: normalize the arranged vector.
        var n = a.Normalize();
        // Assert: the result is the expected unit vector with unit length.
        Assert.Equal(new Vector4d(0, 1, 0, 0), n);
        Assert.Equal(1.0, n.Length(), 12);
    }

    /// <summary>Keeps the zero vector at zero when normalized.</summary>
    [Fact]
    public void Normalize_Zero_Returns_Zero()
    {
        // Arrange: use the zero vector as the normalization input.
        // Act and Assert: normalize zero inline..
        Assert.Equal(Vector4d.Zero, Vector4d.Zero.Normalize());
    }
    #endregion
    #endregion
}
