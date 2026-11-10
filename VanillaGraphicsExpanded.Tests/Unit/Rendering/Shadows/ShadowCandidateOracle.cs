using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using System.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Exhaustively checks small domains with box/segment intersection, independently of the GPU slab sweep.</summary>
internal static class ShadowCandidateOracle
{
    /// <summary>Enumerates cells whose closed boxes may intersect any receiver-to-source segment.</summary>
    internal static HashSet<(int X, int Y, int Z)> Point(ShadowChunkDomain domain, ShadowBounds receiver,
        ShadowBounds source)
    {
        var result = new HashSet<(int, int, int)>();
        for (int z = domain.MinZ; z <= domain.MaxZ; z++)
        for (int y = domain.MinY; y <= domain.MaxY; y++)
        for (int x = domain.MinX; x <= domain.MaxX; x++)
        {
            var cell = new ShadowBounds(x, y, z, x + 1.0, y + 1.0, z + 1.0);
            if (SegmentHullIntersects(receiver, source, cell)) result.Add((x, y, z));
        }
        return result;
    }

    /// <summary>Enumerates cells not proven outside the full spherical-cap swept volume.</summary>
    internal static HashSet<(int X, int Y, int Z)> Directional(ShadowChunkDomain domain,
        ShadowBounds receiver, double dx, double dy, double dz, double radiusDegrees)
    {
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double[] axis = [dx / length, dy / length, dz / length];
        double cosine = Math.Cos(radiusDegrees * Math.PI / 180);
        var result = new HashSet<(int, int, int)>();
        for (int z = domain.MinZ; z <= domain.MaxZ; z++)
        for (int y = domain.MinY; y <= domain.MaxY; y++)
        for (int x = domain.MinX; x <= domain.MaxX; x++)
        {
            var cell = new ShadowBounds(x, y, z, x + 1.0, y + 1.0, z + 1.0);
            double[] low = [cell.MinX - receiver.MaxX, cell.MinY - receiver.MaxY, cell.MinZ - receiver.MaxZ];
            double[] high = [cell.MaxX - receiver.MinX, cell.MaxY - receiver.MinY, cell.MaxZ - receiver.MinZ];
            if (!ProvenOutside(low, high, axis, cosine, 0)) result.Add((x, y, z));
        }
        return result;
    }

    #region Private
    /// <summary>Intersects exact rational parameter intervals derived from the supplied binary doubles.</summary>
    private static bool SegmentHullIntersects(ShadowBounds receiver, ShadowBounds source, ShadowBounds cell)
    {
        var minimum = Rational.Zero;
        var maximum = Rational.One;
        return Overlap(Rational.FromDouble(receiver.MinX), Rational.FromDouble(source.MinX),
                   Rational.FromDouble(receiver.MaxX), Rational.FromDouble(source.MaxX),
                   Rational.FromDouble(cell.MinX), Rational.FromDouble(cell.MaxX),
                   ref minimum, ref maximum)
            && Overlap(Rational.FromDouble(receiver.MinY), Rational.FromDouble(source.MinY),
                   Rational.FromDouble(receiver.MaxY), Rational.FromDouble(source.MaxY),
                   Rational.FromDouble(cell.MinY), Rational.FromDouble(cell.MaxY),
                   ref minimum, ref maximum)
            && Overlap(Rational.FromDouble(receiver.MinZ), Rational.FromDouble(source.MinZ),
                   Rational.FromDouble(receiver.MaxZ), Rational.FromDouble(source.MaxZ),
                   Rational.FromDouble(cell.MinZ), Rational.FromDouble(cell.MaxZ),
                   ref minimum, ref maximum)
            && minimum <= maximum;
    }

    /// <summary>Clips the segment parameter against lower and upper interpolated box inequalities.</summary>
    private static bool Overlap(Rational receiverLow, Rational sourceLow, Rational receiverHigh,
        Rational sourceHigh, Rational cellLow, Rational cellHigh, ref Rational minimum, ref Rational maximum)
    {
        return AtMost(receiverLow, sourceLow - receiverLow, cellHigh, ref minimum, ref maximum)
            && AtLeast(receiverHigh, sourceHigh - receiverHigh, cellLow, ref minimum, ref maximum);
    }

    /// <summary>Restricts a parameter interval for a linear expression that must not exceed a bound.</summary>
    private static bool AtMost(Rational origin, Rational slope, Rational bound,
        ref Rational minimum, ref Rational maximum)
    {
        if (slope.Numerator.IsZero) return origin <= bound;
        Rational crossing = (bound - origin) / slope;
        if (slope > Rational.Zero) maximum = Rational.Min(maximum, crossing);
        else minimum = Rational.Max(minimum, crossing);
        return minimum <= maximum;
    }

    /// <summary>Restricts a parameter interval for a linear expression that must meet a lower bound.</summary>
    private static bool AtLeast(Rational origin, Rational slope, Rational bound,
        ref Rational minimum, ref Rational maximum)
    {
        if (slope.Numerator.IsZero) return origin >= bound;
        Rational crossing = (bound - origin) / slope;
        if (slope > Rational.Zero) minimum = Rational.Max(minimum, crossing);
        else maximum = Rational.Min(maximum, crossing);
        return minimum <= maximum;
    }

    /// <summary>Represents exact fractions so every crossing and interval comparison is free of rounding.</summary>
    private readonly record struct Rational(BigInteger Numerator, BigInteger Denominator) : IComparable<Rational>
    {
        /// <summary>Gets the exact zero fraction.</summary>
        public static Rational Zero => new(BigInteger.Zero, BigInteger.One);

        /// <summary>Gets the exact unit fraction.</summary>
        public static Rational One => new(BigInteger.One, BigInteger.One);

        /// <summary>Converts a finite IEEE-754 value to its exact signed binary rational.</summary>
        public static Rational FromDouble(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            bool negative = bits < 0;
            int exponentBits = (int)((bits >> 52) & 0x7ff);
            ulong fractionBits = (ulong)bits & 0x000f_ffff_ffff_ffffUL;
            if (exponentBits == 0x7ff) throw new ArgumentOutOfRangeException(nameof(value));
            if (exponentBits == 0 && fractionBits == 0) return Zero;

            BigInteger significand = exponentBits == 0 ? fractionBits : fractionBits | (1UL << 52);
            int exponent = exponentBits == 0 ? -1074 : exponentBits - 1023 - 52;
            if (negative) significand = -significand;
            return exponent >= 0
                ? new(significand << exponent, BigInteger.One)
                : new(significand, BigInteger.One << -exponent);
        }

        /// <summary>Compares fractions by exact cross multiplication.</summary>
        public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

        /// <summary>Returns the lesser exact fraction.</summary>
        public static Rational Min(Rational left, Rational right) => left <= right ? left : right;

        /// <summary>Returns the greater exact fraction.</summary>
        public static Rational Max(Rational left, Rational right) => left >= right ? left : right;

        /// <summary>Subtracts two exact fractions without floating-point conversion.</summary>
        public static Rational operator -(Rational left, Rational right) =>
            new(left.Numerator * right.Denominator - right.Numerator * left.Denominator,
                left.Denominator * right.Denominator);

        /// <summary>Divides exact fractions, retaining a positive denominator.</summary>
        public static Rational operator /(Rational left, Rational right)
        {
            BigInteger numerator = left.Numerator * right.Denominator;
            BigInteger denominator = left.Denominator * right.Numerator;
            return denominator.Sign < 0 ? new(-numerator, -denominator) : new(numerator, denominator);
        }

        /// <summary>Tests exact rational ordering.</summary>
        public static bool operator <(Rational left, Rational right) => left.CompareTo(right) < 0;
        /// <summary>Tests exact rational ordering.</summary>
        public static bool operator >(Rational left, Rational right) => left.CompareTo(right) > 0;
        /// <summary>Tests exact rational ordering.</summary>
        public static bool operator <=(Rational left, Rational right) => left.CompareTo(right) <= 0;
        /// <summary>Tests exact rational ordering.</summary>
        public static bool operator >=(Rational left, Rational right) => left.CompareTo(right) >= 0;
    }

    /// <summary>Uses an outward interval upper bound on the cap predicate, subdividing only ambiguous boxes.</summary>
    private static bool ProvenOutside(double[] low, double[] high, double[] axis, double cosine, int depth)
    {
        double dotMaximum = 0, normMinimumSquared = 0, normMaximumSquared = 0;
        for (int i = 0; i < 3; i++)
        {
            double componentMaximum = axis[i] >= 0 ? axis[i] * high[i] : axis[i] * low[i];
            dotMaximum += componentMaximum;
            double nearest = low[i] > 0 ? low[i] : high[i] < 0 ? high[i] : 0;
            double farthest = Math.Max(Math.Abs(low[i]), Math.Abs(high[i]));
            normMinimumSquared += nearest * nearest;
            normMaximumSquared += farthest * farthest;
        }
        double norm = cosine >= 0 ? Math.Sqrt(normMinimumSquared) : Math.Sqrt(normMaximumSquared);
        double upperPredicate = dotMaximum - cosine * norm;
        double outwardAllowance = 1e-12 * (1 + Math.Abs(dotMaximum) + Math.Abs(norm));
        if (upperPredicate < -outwardAllowance) return true;
        if (depth == 24) return false;

        // Split the longest difference interval. This is a geometric branch bound, not an axial slab walk.
        int split = (high[1] - low[1]) > (high[0] - low[0]) ? 1 : 0;
        if ((high[2] - low[2]) > (high[split] - low[split])) split = 2;
        if (high[split] <= low[split]) return false;
        double middle = (low[split] + high[split]) * 0.5;
        double[] leftHigh = (double[])high.Clone(), rightLow = (double[])low.Clone();
        leftHigh[split] = middle;
        rightLow[split] = middle;
        return ProvenOutside(low, leftHigh, axis, cosine, depth + 1)
            && ProvenOutside(rightLow, high, axis, cosine, depth + 1);
    }
    #endregion
}
