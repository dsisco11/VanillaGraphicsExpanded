using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Anchors conservative spatial coverage independently of any rendered-depth projection.</summary>
internal sealed record ShadowCandidateRequest
{
    internal ShadowSourceKey Source { get; private init; }
    internal long WorldOwner { get; private init; }
    internal long DomainGeneration { get; private init; }
    internal long InfluenceGeneration { get; private init; }
    internal long ChangeEpoch { get; private init; }
    internal long ChangeSequence { get; private init; }
    internal ShadowChunkDomain Domain { get; private init; }
    internal ShadowBounds Receiver { get; private init; }
    internal ShadowBounds SourceBox { get; private init; }
    internal Vector3d Direction { get; private init; }
    internal double RadiusDegrees { get; private init; }
    internal bool IsPoint { get; private init; }
    internal ImmutableArray<ShadowCandidateSector> Sectors { get; private init; }

    #region Public API
    /// <summary>Bounds the entire spherical cap analytically, including extrema in its interior.</summary>
    internal static ShadowCandidateRequest Directional(ShadowSourceKey source, long worldOwner,
        long domainGeneration, long influenceGeneration, ShadowChunkDomain domain, ShadowBounds receiver,
        Vector3d direction, double radiusDegrees, long changeEpoch = 1, long changeSequence = 0)
    {
        Validate(source, worldOwner, domainGeneration, influenceGeneration, domain, receiver, changeEpoch, changeSequence);
        var unit = Normalize(direction);
        if (!double.IsFinite(radiusDegrees) || radiusDegrees <= 0 || radiusDegrees >= 180)
            throw new ArgumentOutOfRangeException(nameof(radiusDegrees));
        double angle = radiusDegrees * Math.PI / 180;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        double[] axis = [unit.X, unit.Y, unit.Z], lo = new double[3], hi = new double[3];
        for (int i = 0; i < 3; i++)
        {
            // A component reaches +/-1 inside the cap when the corresponding pole is covered.
            double tangent = Math.Sqrt(Math.Max(0, 1 - axis[i] * axis[i])) * s;
            lo[i] = axis[i] <= -c ? -1 : axis[i] * c - tangent;
            hi[i] = axis[i] >= c ? 1 : axis[i] * c + tangent;
            lo[i] -= 1e-12; hi[i] += 1e-12; // Outward allowance for normalization and libm evaluation.
        }
        int dominant = Math.Abs(axis[1]) > Math.Abs(axis[0]) ? 1 : 0;
        if (Math.Abs(axis[2]) > Math.Abs(axis[dominant])) dominant = 2;
        int sign = axis[dominant] < 0 ? -1 : 1;
        double lower = sign > 0 ? lo[dominant] : -hi[dominant];
        var sectors = ImmutableArray.CreateBuilder<ShadowCandidateSector>();
        if (lower >= 0.25)
            sectors.Add(Sector(dominant, sign, lower, sign > 0 ? hi[dominant] : -lo[dominant], lo, hi, false));
        else
        {
            // Six overlapping dominant sectors cover the sphere. On each, |axial| >= 1/sqrt(3)
            // and both transverse slopes lie in [-1,1]; clipping independent cap bounds is conservative.
            for (int q = 0; q < 3; q++) for (int polarity = -1; polarity <= 1; polarity += 2)
            {
                double maximum = polarity > 0 ? hi[q] : -lo[q];
                double minimum = Math.Max(1 / Math.Sqrt(3) - 1e-12, polarity > 0 ? lo[q] : -hi[q]);
                if (maximum >= minimum) sectors.Add(Sector(q, polarity, minimum, maximum, lo, hi, true));
            }
        }
        return new() { Source = source, WorldOwner = worldOwner, DomainGeneration = domainGeneration,
            InfluenceGeneration = influenceGeneration, Domain = domain, Receiver = receiver, Direction = unit,
            RadiusDegrees = radiusDegrees, Sectors = sectors.ToImmutable(), ChangeEpoch = changeEpoch, ChangeSequence = changeSequence };
    }

    /// <summary>Anchors finite segments between the complete receiver and source movement boxes.</summary>
    internal static ShadowCandidateRequest Point(ShadowSourceKey source, long worldOwner, long domainGeneration,
        long influenceGeneration, ShadowChunkDomain domain, ShadowBounds receiver, ShadowBounds sourceBox,
        long changeEpoch = 1, long changeSequence = 0)
    {
        Validate(source, worldOwner, domainGeneration, influenceGeneration, domain, receiver, changeEpoch, changeSequence);
        if (!Bounded(sourceBox, domain)) throw new ArgumentOutOfRangeException(nameof(sourceBox));
        double[] separation = [Math.Abs(sourceBox.MinX + sourceBox.MaxX - receiver.MinX - receiver.MaxX),
            Math.Abs(sourceBox.MinY + sourceBox.MaxY - receiver.MinY - receiver.MaxY),
            Math.Abs(sourceBox.MinZ + sourceBox.MaxZ - receiver.MinZ - receiver.MaxZ)];
        int axis = separation[1] > separation[0] ? 1 : 0;
        if (separation[2] > separation[axis]) axis = 2;
        return new() { Source = source, WorldOwner = worldOwner, DomainGeneration = domainGeneration,
            InfluenceGeneration = influenceGeneration, Domain = domain, Receiver = receiver, SourceBox = sourceBox,
            IsPoint = true, Sectors = [new(axis, 1, 0, 0, 0, 0)], ChangeEpoch = changeEpoch, ChangeSequence = changeSequence };
    }

    /// <summary>Requires current identity, influence, receiving coverage and motion before resuming or using output.</summary>
    internal bool Covers(ShadowCandidateCurrent current)
    {
        if (current.Source != Source || current.WorldOwner != WorldOwner || current.DomainGeneration != DomainGeneration
            || current.InfluenceGeneration != InfluenceGeneration || current.ChangeEpoch != ChangeEpoch
            || current.ChangeSequence < ChangeSequence || !Receiver.Contains(current.Receiver)) return false;
        var p = current.PositionOrDirection;
        if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)) return false;
        if (IsPoint) return SourceBox.Contains(new(p.X, p.Y, p.Z, p.X, p.Y, p.Z));
        double length = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        return double.IsFinite(length) && length > 0 &&
            (Direction.X * p.X + Direction.Y * p.Y + Direction.Z * p.Z) / length >= Math.Cos(RadiusDegrees * Math.PI / 180);
    }

    /// <summary>Requests replacement in the configured guard; it never extends expired coverage.</summary>
    internal bool NeedsRefresh(ShadowCandidateCurrent current, double guardFraction)
    {
        if (!double.IsFinite(guardFraction) || guardFraction <= 0 || guardFraction >= 1) throw new ArgumentOutOfRangeException(nameof(guardFraction));
        if (!Covers(current)) return true;
        var p = current.PositionOrDirection;
        if (IsPoint)
        {
            double factor = 1 - guardFraction;
            return OutsideInner(p.X, SourceBox.MinX, SourceBox.MaxX, factor)
                || OutsideInner(p.Y, SourceBox.MinY, SourceBox.MaxY, factor)
                || OutsideInner(p.Z, SourceBox.MinZ, SourceBox.MaxZ, factor);
        }
        double length = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        double dot = (Direction.X * p.X + Direction.Y * p.Y + Direction.Z * p.Z) / length;
        return dot <= Math.Cos(RadiusDegrees * (1 - guardFraction) * Math.PI / 180);
    }
    #endregion

    #region Private
    /// <summary>Restricts creation to the validated directional and point factories.</summary>
    private ShadowCandidateRequest() { }
    /// <summary>Rejects unknown dependencies and unrepresentable relative coordinates without shortening the domain.</summary>
    private static void Validate(ShadowSourceKey source, long owner, long domainGeneration, long influenceGeneration,
        ShadowChunkDomain domain, ShadowBounds receiver, long epoch, long sequence)
    {
        if (!source.IsKnown || source.Dimension != domain.Dimension || owner <= 0 || domainGeneration <= 0
            || influenceGeneration <= 0 || epoch <= 0 || sequence < 0 || !domain.IsValid || !Bounded(receiver, domain))
            throw new ArgumentException("Candidate input has unknown identity, invalid bounds or unrepresentable domain.");
    }
    /// <summary>Limits local magnitudes to preserve explicit error bounds while retaining absolute integer identity.</summary>
    private static bool Bounded(ShadowBounds b, ShadowChunkDomain d) => b.IsValid
        && Math.Abs(b.MinX - d.MinX) <= 2097152 && Math.Abs(b.MaxX - d.MinX) <= 2097152
        && Math.Abs(b.MinY - d.MinY) <= 2097152 && Math.Abs(b.MaxY - d.MinY) <= 2097152
        && Math.Abs(b.MinZ - d.MinZ) <= 2097152 && Math.Abs(b.MaxZ - d.MinZ) <= 2097152;
    /// <summary>Normalizes finite nonzero directions before defining angular coverage.</summary>
    private static Vector3d Normalize(Vector3d value)
    {
        double length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        if (!double.IsFinite(length) || length <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return new(value.X / length, value.Y / length, value.Z / length);
    }
    /// <summary>Bounds both independent component quotients and optionally intersects dominant-sector slopes.</summary>
    private static ShadowCandidateSector Sector(int q, int sign, double lower, double upper, double[] lo, double[] hi, bool dominant)
    {
        /// <summary>Divides endpoint bounds by positive denominator extrema, preserving sign-dependent extrema.</summary>
        (double Min, double Max) Ratio(int axis)
        {
            double min = Math.Min(lo[axis] / lower, lo[axis] / upper) - 1e-12;
            double max = Math.Max(hi[axis] / lower, hi[axis] / upper) + 1e-12;
            return dominant ? (Math.Max(-1 - 1e-12, min), Math.Min(1 + 1e-12, max)) : (min, max);
        }
        var a = Ratio((q + 1) % 3); var b = Ratio((q + 2) % 3);
        return new(q, sign, a.Min, a.Max, b.Min, b.Max);
    }
    /// <summary>Tests a closed inner movement box without a division for zero-width source axes.</summary>
    private static bool OutsideInner(double p, double lo, double hi, double factor) => lo == hi
        ? p != lo : Math.Abs(p - (lo + hi) * 0.5) >= (hi - lo) * 0.5 * factor;
    #endregion
}
