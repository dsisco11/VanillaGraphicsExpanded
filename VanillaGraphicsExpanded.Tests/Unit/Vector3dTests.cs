using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class Vector3dTests
{
    [Fact]
    public void Ctor_And_Properties_Work()
    {
        var v = new Vector3d(1.5, -2.25, 3.75);
        Assert.Equal(1.5, v.X);
        Assert.Equal(-2.25, v.Y);
        Assert.Equal(3.75, v.Z);
    }

    [Fact]
    public void ToVector3_And_FromVector3_Work()
    {
        var d = new Vector3d(1.0, 2.0, 3.0);
        Vector3 f = d.ToVector3();
        Assert.Equal(new Vector3(1, 2, 3), f);

        var d2 = Vector3d.FromVector3(new Vector3(4, 5, 6));
        Assert.Equal(new Vector3d(4, 5, 6), d2);
    }

    [Fact]
    public void Deconstruct_Works()
    {
        var v = new Vector3d(7, 8, 9);
        var (x, y, z) = v;
        Assert.Equal(7, x);
        Assert.Equal(8, y);
        Assert.Equal(9, z);
    }

    [Fact]
    public void Equality_Works()
    {
        var a = new Vector3d(1, 2, 3);
        var b = new Vector3d(1, 2, 3);
        var c = new Vector3d(1, 2, 4);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));

        Assert.False(a == c);
        Assert.True(a != c);
    }

    [Fact]
    public void Operators_Work()
    {
        var a = new Vector3d(1, 2, 3);
        var b = new Vector3d(10, 20, 30);

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

    [Fact]
    public void Min_Max_Clamp_Work()
    {
        var a = new Vector3d(1, 10, -5);
        var b = new Vector3d(2, -3, -6);

        Assert.Equal(new Vector3d(1, -3, -6), Vector3d.Min(a, b));
        Assert.Equal(new Vector3d(2, 10, -5), Vector3d.Max(a, b));

        var v = new Vector3d(5, -10, 3);
        var lo = new Vector3d(0, -5, 2);
        var hi = new Vector3d(4, 5, 2);
        Assert.Equal(new Vector3d(4, -5, 2), Vector3d.Clamp(v, lo, hi));
    }

    [Fact]
    public void Dot_Length_Distance_Work()
    {
        var a = new Vector3d(2, 3, 4);
        var b = new Vector3d(5, 6, 7);

        Assert.Equal((2 * 5) + (3 * 6) + (4 * 7), Vector3d.Dot(a, b));
        Assert.Equal((2 * 2) + (3 * 3) + (4 * 4), Vector3d.LengthSquared(a));
        Assert.Equal(Vector3d.LengthSquared(a), a.LengthSquared());

        Assert.Equal(Math.Sqrt(Vector3d.LengthSquared(a)), Vector3d.Length(a), 12);
        Assert.Equal(Vector3d.Length(a), a.Length(), 12);

        Assert.Equal(Vector3d.Length(a - b), Vector3d.Distance(a, b), 12);
        Assert.Equal(Vector3d.LengthSquared(a - b), Vector3d.DistanceSquared(a, b), 12);
    }

    [Fact]
    public void Normalize_Works_For_UnitAxis()
    {
        var a = new Vector3d(0, 3, 0);
        var n = a.Normalize();
        Assert.Equal(new Vector3d(0, 1, 0), n);
        Assert.Equal(1.0, n.Length(), 12);
    }

    [Fact]
    public void Normalize_Zero_Returns_Zero()
    {
        Assert.Equal(Vector3d.Zero, Vector3d.Zero.Normalize());
    }
}
