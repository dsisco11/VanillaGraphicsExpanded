using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit.Numerics;

/// <summary>Verifies the coordinate, conversion, arithmetic, and numeric contracts of <see cref="Vector3d"/>.</summary>
public sealed class Vector3dTests
{
    #region Public API
    #region Construction and conversion
    /// <summary>Creates a vector with the supplied coordinates and exposes them through its properties.</summary>
    [Fact]
    public void Ctor_And_Properties_Work()
    {
        // Arrange: use distinct coordinates so every property can be checked independently.
        // Act: construct the vector from the arranged coordinates.
        var v = new Vector3d(1.5, -2.25, 3.75);
        // Assert: each property exposes its corresponding constructor argument.
        Assert.Equal(1.5, v.X);
        Assert.Equal(-2.25, v.Y);
        Assert.Equal(3.75, v.Z);
    }

    /// <summary>Converts between the project vector and System.Numerics.Vector3 while preserving coordinates.</summary>
    [Fact]
    public void ToVector3_And_FromVector3_Work()
    {
        // Arrange: distinct coordinates make component swaps visible in both conversion directions.
        var d = new Vector3d(1.0, 2.0, 3.0);
        // Act: convert the project vector to the framework vector.
        Vector3 f = d.ToVector3();
        // Assert: the framework value preserves each component.
        Assert.Equal(new Vector3(1, 2, 3), f);

        // Act: convert a separate framework vector back to the project type.
        var d2 = Vector3d.FromVector3(new Vector3(4, 5, 6));
        // Assert: reverse conversion preserves the distinct component values.
        Assert.Equal(new Vector3d(4, 5, 6), d2);
    }

    /// <summary>Returns each coordinate through tuple deconstruction.</summary>
    [Fact]
    public void Deconstruct_Works()
    {
        // Arrange: assign distinct values so tuple coordinate order is observable.
        var v = new Vector3d(7, 8, 9);
        // Act: deconstruct the vector into its coordinate variables.
        var (x, y, z) = v;
        // Assert: each tuple position matches the corresponding vector coordinate.
        Assert.Equal(7, x);
        Assert.Equal(8, y);
        Assert.Equal(9, z);
    }
    #endregion

    #region Equality and arithmetic
    /// <summary>Distinguishes equal and differing coordinate values with the equality operators.</summary>
    [Fact]
    public void Equality_Works()
    {
        // Arrange: prepare identical vectors and a second vector with one changed coordinate.
        var a = new Vector3d(1, 2, 3);
        var b = new Vector3d(1, 2, 3);
        var c = new Vector3d(1, 2, 4);

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
        var a = new Vector3d(1, 2, 3);
        var b = new Vector3d(10, 20, 30);

        // Act and Assert: evaluate vector, scalar, unary, and componentwise operators inline..
        Assert.Equal(new Vector3d(11, 22, 33), a + b);
        Assert.Equal(new Vector3d(-9, -18, -27), a - b);

        Assert.Equal(new Vector3d(6, 7, 8), a + 5);
        Assert.Equal(new Vector3d(6, 7, 8), 5 + a);

        Assert.Equal(new Vector3d(-4, -3, -2), a - 5);
        Assert.Equal(new Vector3d(4, 3, 2), 5 - a);

        Assert.Equal(a, +a);
        Assert.Equal(new Vector3d(-1, -2, -3), -a);

        Assert.Equal(new Vector3d(2, 4, 6), a * 2);
        Assert.Equal(new Vector3d(2, 4, 6), 2 * a);
        Assert.Equal(new Vector3d(10, 40, 90), a * b);

        Assert.Equal(new Vector3d(0.5, 1, 1.5), a / 2);
        Assert.Equal(new Vector3d(0.1, 0.1, 0.1), a / new Vector3d(10, 20, 30));
    }
    #endregion

    #region Coordinate transforms
    /// <summary>Selects componentwise minima and maxima and clamps coordinates to the supplied bounds.</summary>
    [Fact]
    public void Min_Max_Clamp_Work()
    {
        // Arrange: reverse coordinate ordering so Min and Max must select independently per axis.
        var a = new Vector3d(1, 10, -5);
        var b = new Vector3d(2, -3, -6);

        // Act and assert the independently expected coordinate extrema.
        Assert.Equal(new Vector3d(1, -3, -6), Vector3d.Min(a, b));
        Assert.Equal(new Vector3d(2, 10, -5), Vector3d.Max(a, b));

        // Arrange: cross lower and upper bounds and include an equal lower/upper bound on Z.
        var v = new Vector3d(5, -10, 3);
        var lo = new Vector3d(0, -5, 2);
        var hi = new Vector3d(4, 5, 2);
        // Act: clamp the value against these bounds.
        // Assert: X and Y hit opposite bounds while Z remains at its shared bound.
        Assert.Equal(new Vector3d(4, -5, 2), Vector3d.Clamp(v, lo, hi));
    }

    /// <summary>Returns the absolute value of each coordinate.</summary>
    [Fact]
    public void Abs_Works()
    {
        // Arrange: include negative and positive coordinates to exercise both absolute-value paths.
        // Act and Assert: apply the componentwise absolute-value operation inline..
        Assert.Equal(new Vector3d(1.5, 2.25, 3.75), Vector3d.Abs(new Vector3d(-1.5, 2.25, -3.75)));
    }

    /// <summary>Rounds every coordinate down to the greatest integer.</summary>
    [Fact]
    public void Floor_Works()
    {
        // Arrange: include positive and negative fractions plus an exact integer.
        var v = new Vector3d(1.9, -2.1, 3.0);
        // Act and Assert: apply floor to each coordinate inline..
        Assert.Equal(new Vector3d(1, -3, 3), Vector3d.Floor(v));
    }

    /// <summary>Converts floored coordinates to a signed integer vector.</summary>
    [Fact]
    public void FloorToInt3_Works()
    {
        // Arrange: include positive and negative fractions to expose the floor-before-conversion rule.
        var v = new Vector3d(1.9, -2.1, 3.0);
        // Act and Assert: floor and convert the coordinates inline..
        Assert.Equal(new VectorInt3(1, -3, 3), Vector3d.FloorToInt3(v));
    }
    #endregion

    #region Geometry
    /// <summary>Returns the index of the smallest coordinate for each axis position.</summary>
    [Fact]
    public void ArgMinAxis_SelectsSmallestComponent()
    {
        // Arrange: place the smallest value on each of the three axes in turn.
        // Act and Assert: select the minimum axis for each arrangement inline..
        Assert.Equal(0, Vector3d.ArgMinAxis(new Vector3d(1, 2, 3)));
        Assert.Equal(1, Vector3d.ArgMinAxis(new Vector3d(3, 1, 2)));
        Assert.Equal(2, Vector3d.ArgMinAxis(new Vector3d(3, 2, 1)));
    }

    /// <summary>Uses the tracer's documented axis priority when coordinates tie.</summary>
    [Fact]
    public void ArgMinAxis_TieBreaking_MatchesTracerLogic()
    {
        // Arrange: prepare pairwise and three-way minimum ties.
        // X ties with Z => Z
        // Act and Assert: evaluate each tie arrangement with ArgMinAxis inline..
        Assert.Equal(2, Vector3d.ArgMinAxis(new Vector3d(1, 2, 1)));
        // Y ties with Z => Z
        Assert.Equal(2, Vector3d.ArgMinAxis(new Vector3d(2, 1, 1)));
        // X ties with Y (both < Z) => Y
        Assert.Equal(1, Vector3d.ArgMinAxis(new Vector3d(1, 1, 2)));
        // X ties with Y ties with Z => Z
        Assert.Equal(2, Vector3d.ArgMinAxis(new Vector3d(1, 1, 1)));
    }

    /// <summary>Computes dot product, lengths, and distances from the vector coordinates.</summary>
    [Fact]
    public void Dot_Length_Distance_Work()
    {
        // Arrange: choose small coordinates for direct dot, length, and distance calculations.
        var a = new Vector3d(2, 3, 4);
        var b = new Vector3d(5, 6, 7);

        // Act and Assert: evaluate each static and instance measurement inline..
        Assert.Equal((2 * 5) + (3 * 6) + (4 * 7), Vector3d.Dot(a, b));
        Assert.Equal((2 * 2) + (3 * 3) + (4 * 4), Vector3d.LengthSquared(a));
        Assert.Equal(Vector3d.LengthSquared(a), a.LengthSquared());

        Assert.Equal(Math.Sqrt(Vector3d.LengthSquared(a)), Vector3d.Length(a), 12);
        Assert.Equal(Vector3d.Length(a), a.Length(), 12);

        Assert.Equal(Vector3d.Length(a - b), Vector3d.Distance(a, b), 12);
        Assert.Equal(Vector3d.LengthSquared(a - b), Vector3d.DistanceSquared(a, b), 12);
    }

    /// <summary>Normalizes a nonzero vector to unit length.</summary>
    [Fact]
    public void Normalize_Works_For_UnitAxis()
    {
        // Arrange: choose a non-unit vector aligned with one axis.
        var a = new Vector3d(0, 3, 0);
        // Act: normalize the arranged vector.
        var n = a.Normalize();
        // Assert: the result is the expected unit vector with unit length.
        Assert.Equal(new Vector3d(0, 1, 0), n);
        Assert.Equal(1.0, n.Length(), 12);
    }

    /// <summary>Keeps the zero vector at zero when normalized.</summary>
    [Fact]
    public void Normalize_Zero_Returns_Zero()
    {
        // Arrange: use the zero vector as the normalization input.
        // Act and Assert: normalize zero inline..
        Assert.Equal(Vector3d.Zero, Vector3d.Zero.Normalize());
    }
    #endregion
    #endregion
}
