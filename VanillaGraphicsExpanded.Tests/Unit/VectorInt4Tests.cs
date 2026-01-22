using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class VectorInt4Tests
{
    [Fact]
    public void Ctor_And_Properties_Work()
    {
        var v = new VectorInt4(1, -2, 3, -4);
        Assert.Equal(1, v.X);
        Assert.Equal(-2, v.Y);
        Assert.Equal(3, v.Z);
        Assert.Equal(-4, v.W);
    }

    [Fact]
    public void ToVector4_Converts_Components()
    {
        var v = new VectorInt4(2, 3, 4, 5);
        Vector4 f = v.ToVector4();
        Assert.Equal(new Vector4(2, 3, 4, 5), f);
    }

    [Fact]
    public void Deconstruct_Works()
    {
        var v = new VectorInt4(7, 8, 9, 10);
        var (x, y, z, w) = v;
        Assert.Equal(7, x);
        Assert.Equal(8, y);
        Assert.Equal(9, z);
        Assert.Equal(10, w);
    }

    [Fact]
    public void Equality_Works()
    {
        var a = new VectorInt4(1, 2, 3, 4);
        var b = new VectorInt4(1, 2, 3, 4);
        var c = new VectorInt4(1, 2, 3, 5);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));

        Assert.False(a == c);
        Assert.True(a != c);
    }

    [Fact]
    public void Add_Sub_MixedScalar_Works()
    {
        var a = new VectorInt4(1, 2, 3, 4);
        var b = new VectorInt4(10, 20, 30, 40);

        Assert.Equal(new VectorInt4(11, 22, 33, 44), a + b);
        Assert.Equal(new VectorInt4(-9, -18, -27, -36), a - b);

        Assert.Equal(new VectorInt4(6, 7, 8, 9), a + 5);
        Assert.Equal(new VectorInt4(6, 7, 8, 9), 5 + a);

        Assert.Equal(new VectorInt4(-4, -3, -2, -1), a - 5);
        Assert.Equal(new VectorInt4(4, 3, 2, 1), 5 - a);

        Assert.Equal(a, +a);
        Assert.Equal(new VectorInt4(-1, -2, -3, -4), -a);
    }

    [Fact]
    public void Multiply_Works()
    {
        var a = new VectorInt4(2, -3, 4, -5);
        var b = new VectorInt4(10, 2, -5, -2);

        Assert.Equal(new VectorInt4(20, -6, -20, 10), a * b);
        Assert.Equal(new VectorInt4(6, -9, 12, -15), a * 3);
        Assert.Equal(new VectorInt4(6, -9, 12, -15), 3 * a);
    }

    [Fact]
    public void Divide_And_Modulo_Work()
    {
        var a = new VectorInt4(10, -9, 8, -12);
        var b = new VectorInt4(2, 3, -4, -3);

        Assert.Equal(new VectorInt4(5, -3, -2, 4), a / b);
        Assert.Equal(new VectorInt4(3, -3, 2, -4), a / 3);

        Assert.Equal(new VectorInt4(0, 0, 0, 0), a % b);
        Assert.Equal(new VectorInt4(1, 0, 2, 0), a % 3);
    }

    [Fact]
    public void BitwiseOps_Work()
    {
        var a = new VectorInt4(0b1010, 0b1100, 0b0110, 0b1111);
        var b = new VectorInt4(0b0110, 0b1010, 0b0101, 0b0011);

        Assert.Equal(new VectorInt4(0b0010, 0b1000, 0b0100, 0b0011), a & b);
        Assert.Equal(new VectorInt4(0b1110, 0b1110, 0b0111, 0b1111), a | b);
        Assert.Equal(new VectorInt4(0b1100, 0b0110, 0b0011, 0b1100), a ^ b);

        Assert.Equal(new VectorInt4(~0b1010, ~0b1100, ~0b0110, ~0b1111), ~a);
    }

    [Fact]
    public void Shifts_Work()
    {
        var a = new VectorInt4(1, -2, 3, -4);

        Assert.Equal(new VectorInt4(2, -4, 6, -8), a << 1);
        Assert.Equal(new VectorInt4(0, -1, 1, -2), a >> 1);

        Assert.Equal(a, a << 0);
        Assert.Equal(a, a >> 0);
    }

    [Fact]
    public void Inc_Dec_Work()
    {
        var a = new VectorInt4(1, 2, 3, 4);
        Assert.Equal(new VectorInt4(2, 3, 4, 5), ++a);
        Assert.Equal(new VectorInt4(1, 2, 3, 4), --a);
    }

    [Fact]
    public void Min_Max_Clamp_Work()
    {
        var a = new VectorInt4(1, 10, -5, 3);
        var b = new VectorInt4(2, -3, -6, 7);

        Assert.Equal(new VectorInt4(1, -3, -6, 3), VectorInt4.Min(a, b));
        Assert.Equal(new VectorInt4(2, 10, -5, 7), VectorInt4.Max(a, b));

        var v = new VectorInt4(5, -10, 3, 9);
        var lo = new VectorInt4(0, -5, 2, 8);
        var hi = new VectorInt4(4, 5, 2, 8);
        Assert.Equal(new VectorInt4(4, -5, 2, 8), VectorInt4.Clamp(v, lo, hi));
    }

    [Fact]
    public void Dot_And_LengthSquared_Work()
    {
        var a = new VectorInt4(2, 3, 4, 5);
        var b = new VectorInt4(5, 6, 7, 8);

        Assert.Equal((2 * 5) + (3 * 6) + (4 * 7) + (5 * 8), VectorInt4.Dot(a, b));
        Assert.Equal((2 * 2) + (3 * 3) + (4 * 4) + (5 * 5), VectorInt4.LengthSquared(a));
        Assert.Equal(VectorInt4.LengthSquared(a), a.LengthSquared());
    }
}
