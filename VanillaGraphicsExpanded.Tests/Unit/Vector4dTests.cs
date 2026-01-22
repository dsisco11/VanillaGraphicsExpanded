using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class Vector4dTests
{
    [Fact]
    public void Ctor_And_Properties_Work()
    {
        var v = new Vector4d(1.5, -2.25, 3.75, -4.5);
        Assert.Equal(1.5, v.X);
        Assert.Equal(-2.25, v.Y);
        Assert.Equal(3.75, v.Z);
        Assert.Equal(-4.5, v.W);
    }

    [Fact]
    public void ToVector4_And_FromVector4_Work()
    {
        var d = new Vector4d(1.0, 2.0, 3.0, 4.0);
        Vector4 f = d.ToVector4();
        Assert.Equal(new Vector4(1, 2, 3, 4), f);

        var d2 = Vector4d.FromVector4(new Vector4(4, 5, 6, 7));
        Assert.Equal(new Vector4d(4, 5, 6, 7), d2);
    }

    [Fact]
    public void Deconstruct_Works()
    {
        var v = new Vector4d(7, 8, 9, 10);
        var (x, y, z, w) = v;
        Assert.Equal(7, x);
        Assert.Equal(8, y);
        Assert.Equal(9, z);
        Assert.Equal(10, w);
    }

    [Fact]
    public void Equality_Works()
    {
        var a = new Vector4d(1, 2, 3, 4);
        var b = new Vector4d(1, 2, 3, 4);
        var c = new Vector4d(1, 2, 3, 5);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));

        Assert.False(a == c);
        Assert.True(a != c);
    }

    [Fact]
    public void Operators_Work()
    {
        var a = new Vector4d(1, 2, 3, 4);
        var b = new Vector4d(10, 20, 30, 40);

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

    [Fact]
    public void Min_Max_Clamp_Work()
    {
        var a = new Vector4d(1, 10, -5, 3);
        var b = new Vector4d(2, -3, -6, 7);

        Assert.Equal(new Vector4d(1, -3, -6, 3), Vector4d.Min(a, b));
        Assert.Equal(new Vector4d(2, 10, -5, 7), Vector4d.Max(a, b));

        var v = new Vector4d(5, -10, 3, 9);
        var lo = new Vector4d(0, -5, 2, 8);
        var hi = new Vector4d(4, 5, 2, 8);
        Assert.Equal(new Vector4d(4, -5, 2, 8), Vector4d.Clamp(v, lo, hi));
    }

    [Fact]
    public void Dot_Length_Distance_Work()
    {
        var a = new Vector4d(2, 3, 4, 5);
        var b = new Vector4d(5, 6, 7, 8);

        Assert.Equal((2 * 5) + (3 * 6) + (4 * 7) + (5 * 8), Vector4d.Dot(a, b));
        Assert.Equal((2 * 2) + (3 * 3) + (4 * 4) + (5 * 5), Vector4d.LengthSquared(a));
        Assert.Equal(Vector4d.LengthSquared(a), a.LengthSquared());

        Assert.Equal(Math.Sqrt(Vector4d.LengthSquared(a)), Vector4d.Length(a), 12);
        Assert.Equal(Vector4d.Length(a), a.Length(), 12);

        Assert.Equal(Vector4d.Length(a - b), Vector4d.Distance(a, b), 12);
        Assert.Equal(Vector4d.LengthSquared(a - b), Vector4d.DistanceSquared(a, b), 12);
    }

    [Fact]
    public void Normalize_Works_For_UnitAxis()
    {
        var a = new Vector4d(0, 3, 0, 0);
        var n = a.Normalize();
        Assert.Equal(new Vector4d(0, 1, 0, 0), n);
        Assert.Equal(1.0, n.Length(), 12);
    }

    [Fact]
    public void Normalize_Zero_Returns_Zero()
    {
        Assert.Equal(Vector4d.Zero, Vector4d.Zero.Normalize());
    }
}
