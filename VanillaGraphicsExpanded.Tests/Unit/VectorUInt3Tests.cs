using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class VectorUInt3Tests
{
    [Fact]
    public void Ctor_And_Properties_Work()
    {
        var v = new VectorUInt3(1, 2, 3);
        Assert.Equal(1u, v.X);
        Assert.Equal(2u, v.Y);
        Assert.Equal(3u, v.Z);
    }

    [Fact]
    public void ToVector3_Converts_Components()
    {
        var v = new VectorUInt3(2, 3, 4);
        Vector3 f = v.ToVector3();
        Assert.Equal(new Vector3(2, 3, 4), f);
    }

    [Fact]
    public void Deconstruct_Works()
    {
        var v = new VectorUInt3(7, 8, 9);
        var (x, y, z) = v;
        Assert.Equal(7u, x);
        Assert.Equal(8u, y);
        Assert.Equal(9u, z);
    }

    [Fact]
    public void Equality_Works()
    {
        var a = new VectorUInt3(1, 2, 3);
        var b = new VectorUInt3(1, 2, 3);
        var c = new VectorUInt3(1, 2, 4);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));

        Assert.False(a == c);
        Assert.True(a != c);
    }

    [Fact]
    public void Add_Sub_MixedScalar_Works()
    {
        var a = new VectorUInt3(1, 2, 3);
        var b = new VectorUInt3(10, 20, 30);

        Assert.Equal(new VectorUInt3(11, 22, 33), a + b);

        Assert.Equal(new VectorUInt3(6, 7, 8), a + 5u);
        Assert.Equal(new VectorUInt3(6, 7, 8), 5u + a);

        Assert.Equal(new VectorUInt3(0, 1, 2), a - 1u);
        Assert.Equal(new VectorUInt3(4, 3, 2), 5u - a);

        Assert.Equal(a, +a);
    }

    [Fact]
    public void Negation_Wraps()
    {
        var a = new VectorUInt3(1, 2, 3);
        Assert.Equal(new VectorUInt3(uint.MaxValue, uint.MaxValue - 1, uint.MaxValue - 2), -a);
    }

    [Fact]
    public void Multiply_Works()
    {
        var a = new VectorUInt3(2, 3, 4);
        var b = new VectorUInt3(10, 2, 5);

        Assert.Equal(new VectorUInt3(20, 6, 20), a * b);
        Assert.Equal(new VectorUInt3(6, 9, 12), a * 3u);
        Assert.Equal(new VectorUInt3(6, 9, 12), 3u * a);
    }

    [Fact]
    public void Divide_And_Modulo_Work()
    {
        var a = new VectorUInt3(10, 9, 8);
        var b = new VectorUInt3(2, 3, 4);

        Assert.Equal(new VectorUInt3(5, 3, 2), a / b);
        Assert.Equal(new VectorUInt3(3, 3, 2), a / 3u);

        Assert.Equal(new VectorUInt3(0, 0, 0), a % b);
        Assert.Equal(new VectorUInt3(1, 0, 2), a % 3u);
    }

    [Fact]
    public void BitwiseOps_Work()
    {
        var a = new VectorUInt3(0b1010, 0b1100, 0b0110);
        var b = new VectorUInt3(0b0110, 0b1010, 0b0101);

        Assert.Equal(new VectorUInt3(0b0010, 0b1000, 0b0100), a & b);
        Assert.Equal(new VectorUInt3(0b1110, 0b1110, 0b0111), a | b);
        Assert.Equal(new VectorUInt3(0b1100, 0b0110, 0b0011), a ^ b);

        Assert.Equal(new VectorUInt3(~0b1010u, ~0b1100u, ~0b0110u), ~a);
    }

    [Fact]
    public void Shifts_Work()
    {
        var a = new VectorUInt3(1, 2, 3);

        Assert.Equal(new VectorUInt3(2, 4, 6), a << 1);
        Assert.Equal(new VectorUInt3(0, 1, 1), a >> 1);

        Assert.Equal(a, a << 0);
        Assert.Equal(a, a >> 0);
    }

    [Fact]
    public void Inc_Dec_Work()
    {
        var a = new VectorUInt3(1, 2, 3);
        Assert.Equal(new VectorUInt3(2, 3, 4), ++a);
        Assert.Equal(new VectorUInt3(1, 2, 3), --a);
    }

    [Fact]
    public void Min_Max_Clamp_Work()
    {
        var a = new VectorUInt3(1, 10, 5);
        var b = new VectorUInt3(2, 3, 6);

        Assert.Equal(new VectorUInt3(1, 3, 5), VectorUInt3.Min(a, b));
        Assert.Equal(new VectorUInt3(2, 10, 6), VectorUInt3.Max(a, b));

        var v = new VectorUInt3(5, 1, 3);
        var lo = new VectorUInt3(0, 2, 2);
        var hi = new VectorUInt3(4, 5, 2);
        Assert.Equal(new VectorUInt3(4, 2, 2), VectorUInt3.Clamp(v, lo, hi));
    }

    [Fact]
    public void Dot_And_LengthSquared_Work()
    {
        var a = new VectorUInt3(2, 3, 4);
        var b = new VectorUInt3(5, 6, 7);

        Assert.Equal(unchecked((2u * 5u) + (3u * 6u) + (4u * 7u)), VectorUInt3.Dot(a, b));
        Assert.Equal(unchecked((2u * 2u) + (3u * 3u) + (4u * 4u)), VectorUInt3.LengthSquared(a));
        Assert.Equal(VectorUInt3.LengthSquared(a), a.LengthSquared());
    }
}
