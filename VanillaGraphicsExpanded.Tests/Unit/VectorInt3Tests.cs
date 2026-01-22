using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class VectorInt3Tests
{
    [Fact]
    public void Ctor_And_Properties_Work()
    {
        var v = new VectorInt3(1, -2, 3);
        Assert.Equal(1, v.X);
        Assert.Equal(-2, v.Y);
        Assert.Equal(3, v.Z);
    }

    [Fact]
    public void ToVector3_Converts_Components()
    {
        var v = new VectorInt3(2, 3, 4);
        Vector3 f = v.ToVector3();
        Assert.Equal(new Vector3(2, 3, 4), f);
    }

    [Fact]
    public void Deconstruct_Works()
    {
        var v = new VectorInt3(7, 8, 9);
        var (x, y, z) = v;
        Assert.Equal(7, x);
        Assert.Equal(8, y);
        Assert.Equal(9, z);
    }

    [Fact]
    public void Equality_Works()
    {
        var a = new VectorInt3(1, 2, 3);
        var b = new VectorInt3(1, 2, 3);
        var c = new VectorInt3(1, 2, 4);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));

        Assert.False(a == c);
        Assert.True(a != c);
    }

    [Fact]
    public void Add_Sub_MixedScalar_Works()
    {
        var a = new VectorInt3(1, 2, 3);
        var b = new VectorInt3(10, 20, 30);

        Assert.Equal(new VectorInt3(11, 22, 33), a + b);
        Assert.Equal(new VectorInt3(-9, -18, -27), a - b);

        Assert.Equal(new VectorInt3(6, 7, 8), a + 5);
        Assert.Equal(new VectorInt3(6, 7, 8), 5 + a);

        Assert.Equal(new VectorInt3(-4, -3, -2), a - 5);
        Assert.Equal(new VectorInt3(4, 3, 2), 5 - a);

        Assert.Equal(a, +a);
        Assert.Equal(new VectorInt3(-1, -2, -3), -a);
    }

    [Fact]
    public void Multiply_Works()
    {
        var a = new VectorInt3(2, -3, 4);
        var b = new VectorInt3(10, 2, -5);

        Assert.Equal(new VectorInt3(20, -6, -20), a * b);
        Assert.Equal(new VectorInt3(6, -9, 12), a * 3);
        Assert.Equal(new VectorInt3(6, -9, 12), 3 * a);
    }

    [Fact]
    public void Divide_And_Modulo_Work()
    {
        var a = new VectorInt3(10, -9, 8);
        var b = new VectorInt3(2, 3, -4);

        Assert.Equal(new VectorInt3(5, -3, -2), a / b);
        Assert.Equal(new VectorInt3(3, -3, 2), a / 3);

        Assert.Equal(new VectorInt3(0, 0, 0), a % b);
        Assert.Equal(new VectorInt3(1, 0, 2), a % 3);
    }

    [Fact]
    public void BitwiseOps_Work()
    {
        var a = new VectorInt3(0b1010, 0b1100, 0b0110);
        var b = new VectorInt3(0b0110, 0b1010, 0b0101);

        Assert.Equal(new VectorInt3(0b0010, 0b1000, 0b0100), a & b);
        Assert.Equal(new VectorInt3(0b1110, 0b1110, 0b0111), a | b);
        Assert.Equal(new VectorInt3(0b1100, 0b0110, 0b0011), a ^ b);

        Assert.Equal(new VectorInt3(~0b1010, ~0b1100, ~0b0110), ~a);
    }

    [Fact]
    public void Shifts_Work()
    {
        var a = new VectorInt3(1, -2, 3);

        Assert.Equal(new VectorInt3(2, -4, 6), a << 1);
        Assert.Equal(new VectorInt3(0, -1, 1), a >> 1);

        Assert.Equal(a, a << 0);
        Assert.Equal(a, a >> 0);
    }

    [Fact]
    public void Inc_Dec_Work()
    {
        var a = new VectorInt3(1, 2, 3);
        Assert.Equal(new VectorInt3(2, 3, 4), ++a);
        Assert.Equal(new VectorInt3(1, 2, 3), --a);
    }

    [Fact]
    public void Min_Max_Clamp_Work()
    {
        var a = new VectorInt3(1, 10, -5);
        var b = new VectorInt3(2, -3, -6);

        Assert.Equal(new VectorInt3(1, -3, -6), VectorInt3.Min(a, b));
        Assert.Equal(new VectorInt3(2, 10, -5), VectorInt3.Max(a, b));

        var v = new VectorInt3(5, -10, 3);
        var lo = new VectorInt3(0, -5, 2);
        var hi = new VectorInt3(4, 5, 2);
        Assert.Equal(new VectorInt3(4, -5, 2), VectorInt3.Clamp(v, lo, hi));
    }

    [Fact]
    public void Dot_And_LengthSquared_Work()
    {
        var a = new VectorInt3(2, 3, 4);
        var b = new VectorInt3(5, 6, 7);

        Assert.Equal((2 * 5) + (3 * 6) + (4 * 7), VectorInt3.Dot(a, b));
        Assert.Equal((2 * 2) + (3 * 3) + (4 * 4), VectorInt3.LengthSquared(a));
        Assert.Equal(VectorInt3.LengthSquared(a), a.LengthSquared());
    }
}
