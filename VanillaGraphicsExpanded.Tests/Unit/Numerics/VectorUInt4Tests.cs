using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit.Numerics;

/// <summary>Verifies unsigned coordinate conversion, wrapping arithmetic, bitwise, and numeric contracts of <see cref="VectorUInt4"/>.</summary>
public sealed class VectorUInt4Tests
{
    #region Public API
    #region Construction and conversion
    /// <summary>Creates a vector with the supplied coordinates and exposes them through its properties.</summary>
    [Fact]
    public void Ctor_And_Properties_Work()
    {
        // Arrange: use distinct coordinates so every property can be checked independently.
        // Act: construct the vector from the arranged coordinates.
        var v = new VectorUInt4(1, 2, 3, 4);
        // Assert: each property exposes its corresponding constructor argument.
        Assert.Equal(1u, v.X);
        Assert.Equal(2u, v.Y);
        Assert.Equal(3u, v.Z);
        Assert.Equal(4u, v.W);
    }

    /// <summary>Converts the unsigned integer coordinates to System.Numerics.Vector4.</summary>
    [Fact]
    public void ToVector4_Converts_Components()
    {
        // Arrange: choose distinct coordinates so conversion reveals component ordering.
        var v = new VectorUInt4(2, 3, 4, 5);
        // Act: convert the project vector to System.Numerics.Vector4.
        Vector4 f = v.ToVector4();
        // Assert: the framework vector preserves all coordinate positions.
        Assert.Equal(new Vector4(2, 3, 4, 5), f);
    }

    /// <summary>Returns each coordinate through tuple deconstruction.</summary>
    [Fact]
    public void Deconstruct_Works()
    {
        // Arrange: assign distinct values so tuple coordinate order is observable.
        var v = new VectorUInt4(7, 8, 9, 10);
        // Act: deconstruct the vector into its coordinate variables.
        var (x, y, z, w) = v;
        // Assert: each tuple position matches the corresponding vector coordinate.
        Assert.Equal(7u, x);
        Assert.Equal(8u, y);
        Assert.Equal(9u, z);
        Assert.Equal(10u, w);
    }
    #endregion

    #region Arithmetic
    /// <summary>Distinguishes equal and differing coordinate values with the equality operators.</summary>
    [Fact]
    public void Equality_Works()
    {
        // Arrange: prepare identical vectors and a second vector with one changed coordinate.
        var a = new VectorUInt4(1, 2, 3, 4);
        var b = new VectorUInt4(1, 2, 3, 4);
        var c = new VectorUInt4(1, 2, 3, 5);

        // Act and Assert: compare the equal and differing values through equality operators..
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));

        Assert.False(a == c);
        Assert.True(a != c);
    }

    /// <summary>Applies vector addition, subtraction, and mixed scalar arithmetic componentwise.</summary>
    [Fact]
    public void Add_Sub_MixedScalar_Works()
    {
        // Arrange: use distinct vector and scalar values to expose coordinate-wise addition and subtraction.
        var a = new VectorUInt4(1, 2, 3, 4);
        var b = new VectorUInt4(10, 20, 30, 40);

        // Act and Assert: evaluate vector and mixed-scalar arithmetic inline..
        Assert.Equal(new VectorUInt4(11, 22, 33, 44), a + b);
        Assert.Equal(new VectorUInt4(6, 7, 8, 9), a + 5u);
        Assert.Equal(new VectorUInt4(6, 7, 8, 9), 5u + a);

        Assert.Equal(new VectorUInt4(0, 1, 2, 3), a - 1u);
        Assert.Equal(new VectorUInt4(4, 3, 2, 1), 5u - a);

        Assert.Equal(a, +a);
    }

    /// <summary>Wraps unsigned negation independently for each coordinate.</summary>
    [Fact]
    public void Negation_Wraps()
    {
        // Arrange: use small positive unsigned coordinates so each wrapped result is explicit.
        var a = new VectorUInt4(1, 2, 3, 4);
        // Act and Assert: negate each unsigned coordinate inline..
        Assert.Equal(new VectorUInt4(uint.MaxValue, uint.MaxValue - 1, uint.MaxValue - 2, uint.MaxValue - 3), -a);
    }

    /// <summary>Multiplies vector coordinates componentwise and by a scalar.</summary>
    [Fact]
    public void Multiply_Works()
    {
        // Arrange: choose different coordinates in both operands to expose componentwise multiplication.
        var a = new VectorUInt4(2, 3, 4, 5);
        var b = new VectorUInt4(10, 2, 5, 2);

        // Act and Assert: evaluate vector and scalar multiplication inline..
        Assert.Equal(new VectorUInt4(20, 6, 20, 10), a * b);
        Assert.Equal(new VectorUInt4(6, 9, 12, 15), a * 3u);
        Assert.Equal(new VectorUInt4(6, 9, 12, 15), 3u * a);
    }

    /// <summary>Applies componentwise division and remainder with vector and scalar divisors.</summary>
    [Fact]
    public void Divide_And_Modulo_Work()
    {
        // Arrange: choose exact divisors so quotient and remainder are independently predictable.
        var a = new VectorUInt4(10, 9, 8, 12);
        var b = new VectorUInt4(2, 3, 4, 3);

        // Act and Assert: evaluate vector and scalar division and remainder inline..
        Assert.Equal(new VectorUInt4(5, 3, 2, 4), a / b);
        Assert.Equal(new VectorUInt4(3, 3, 2, 4), a / 3u);

        Assert.Equal(new VectorUInt4(0, 0, 0, 0), a % b);
        Assert.Equal(new VectorUInt4(1, 0, 2, 0), a % 3u);
    }
    #endregion

    #region Bitwise and shifts
    /// <summary>Applies bitwise AND, OR, XOR, and complement independently to each coordinate.</summary>
    [Fact]
    public void BitwiseOps_Work()
    {
        // Arrange: choose bit patterns with overlapping and distinct bits in each coordinate.
        var a = new VectorUInt4(0b1010, 0b1100, 0b0110, 0b1111);
        var b = new VectorUInt4(0b0110, 0b1010, 0b0101, 0b0011);

        // Act and Assert: evaluate AND, OR, XOR, and complement inline..
        Assert.Equal(new VectorUInt4(0b0010, 0b1000, 0b0100, 0b0011), a & b);
        Assert.Equal(new VectorUInt4(0b1110, 0b1110, 0b0111, 0b1111), a | b);
        Assert.Equal(new VectorUInt4(0b1100, 0b0110, 0b0011, 0b1100), a ^ b);

        Assert.Equal(new VectorUInt4(~0b1010u, ~0b1100u, ~0b0110u, ~0b1111u), ~a);
    }

    /// <summary>Shifts each coordinate and preserves values for a zero shift.</summary>
    [Fact]
    public void Shifts_Work()
    {
        // Arrange: use distinct coordinates and test both one-bit and zero-bit shifts.
        var a = new VectorUInt4(1, 2, 3, 4);

        // Act and Assert: evaluate left and right shifts inline..
        Assert.Equal(new VectorUInt4(2, 4, 6, 8), a << 1);
        Assert.Equal(new VectorUInt4(0, 1, 1, 2), a >> 1);

        Assert.Equal(a, a << 0);
        Assert.Equal(a, a >> 0);
    }
    #endregion

    #region Coordinate utilities
    /// <summary>Applies increment and decrement to every coordinate.</summary>
    [Fact]
    public void Inc_Dec_Work()
    {
        // Arrange: start with distinct coordinates so each increment and decrement is visible.
        var a = new VectorUInt4(1, 2, 3, 4);
        // Act and Assert: apply prefix increment and decrement to the vector..
        Assert.Equal(new VectorUInt4(2, 3, 4, 5), ++a);
        Assert.Equal(new VectorUInt4(1, 2, 3, 4), --a);
    }

    /// <summary>Selects componentwise minima and maxima and clamps coordinates to the supplied bounds.</summary>
    [Fact]
    public void Min_Max_Clamp_Work()
    {
        // Arrange: reverse coordinate ordering so Min and Max must select independently per axis.
        var a = new VectorUInt4(1, 10, 5, 3);
        var b = new VectorUInt4(2, 3, 6, 7);

        // Act and assert the independently expected coordinate extrema.
        Assert.Equal(new VectorUInt4(1, 3, 5, 3), VectorUInt4.Min(a, b));
        Assert.Equal(new VectorUInt4(2, 10, 6, 7), VectorUInt4.Max(a, b));

        // Arrange: cross lower and upper bounds and include an equal lower/upper bound on Z.
        var v = new VectorUInt4(5, 1, 3, 9);
        var lo = new VectorUInt4(0, 2, 2, 8);
        var hi = new VectorUInt4(4, 5, 2, 8);
        // Act: clamp the value against these bounds.
        // Assert: coordinates hit lower, upper, and shared bounds as arranged.
        Assert.Equal(new VectorUInt4(4, 2, 2, 8), VectorUInt4.Clamp(v, lo, hi));
    }

    /// <summary>Computes dot product and squared length using the coordinate values.</summary>
    [Fact]
    public void Dot_And_LengthSquared_Work()
    {
        // Arrange: choose small coordinates whose dot product and squared length can be calculated directly.
        var a = new VectorUInt4(2, 3, 4, 5);
        var b = new VectorUInt4(5, 6, 7, 8);

        // Act and Assert: evaluate dot product and squared-length operations inline..
        Assert.Equal(unchecked((2u * 5u) + (3u * 6u) + (4u * 7u) + (5u * 8u)), VectorUInt4.Dot(a, b));
        Assert.Equal(unchecked((2u * 2u) + (3u * 3u) + (4u * 4u) + (5u * 5u)), VectorUInt4.LengthSquared(a));
        Assert.Equal(VectorUInt4.LengthSquared(a), a.LengthSquared());
    }
    #endregion
    #endregion
}
