using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects anchored candidate coverage and its conservative directional sector bounds.</summary>
public sealed class ShadowCandidateRequestTests
{
    private static readonly ShadowChunkDomain Domain = new(0, -4, -3, -5, 5, 4, 6);
    private static readonly ShadowBounds Receiver = new(-1, 0, -2, 0, 1, -1);
    private static readonly ShadowSourceKey Source = new(1, 0, ShadowSourceKind.Sun, 1, 1, 0);

    #region Public API
    /// <summary>Directional requests contain their whole angular cap and request refresh before leaving it.</summary>
    [Fact]
    public void DirectionalCoverageUsesFullCapAndRefreshGuard()
    {
        // Arrange: anchor a narrow cap around a non-axis-aligned direction.
        var request = ShadowCandidateRequest.Directional(Source, 7, 3, 4, Domain, Receiver,
            new Vector3d(1, 2, 3), 12);
        var center = new Vector3d(1, 2, 3);
        var rotationAxis = Cross(center, new Vector3d(0, 1, 0));
        var within = Rotate(center, 11.9, rotationAxis);
        var outside = Rotate(center, 12.1, rotationAxis);
        var current = Current(request, Receiver, within);

        // Act: query coverage and the configured outer-quarter refresh band.
        bool covered = request.Covers(current);
        bool refreshAtCenter = request.NeedsRefresh(Current(request, Receiver, center), .25);
        bool refreshInGuard = request.NeedsRefresh(current, .25);

        // Assert: association validity covers the cap interior; the guard schedules replacement before expiry.
        Assert.True(covered);
        Assert.False(refreshAtCenter);
        Assert.True(refreshInGuard);
        Assert.False(request.Covers(Current(request, Receiver, outside)));
    }

    /// <summary>Projection changes preserve spatial associations while domain and change history changes invalidate them.</summary>
    [Fact]
    public void CandidateDependenciesIgnoreProjectionAndRejectLostCoverage()
    {
        // Arrange: build one point-source interval and matching current dependencies.
        var sourceBox = new ShadowBounds(2, 2, 2, 3, 3, 3);
        var request = ShadowCandidateRequest.Point(Source with { Kind = ShadowSourceKind.Entity }, 7, 3, 4,
            Domain, Receiver, sourceBox, changeEpoch: 9, changeSequence: 21);
        var position = new Vector3d(2.5, 2.5, 2.5);
        var current = Current(request, Receiver, position, projection: 1);

        // Act: alter only projection, then alter the domain/history or enlarge receiver coverage.
        var changedProjection = current with { ProjectionGeneration = 99 };
        var changedDomain = current with { DomainGeneration = current.DomainGeneration + 1 };
        var lostChanges = current with { ChangeEpoch = current.ChangeEpoch + 1 };
        var laggingChanges = current with { ChangeSequence = 20 };
        var largerReceiver = current with { Receiver = new(-1, 0, -2, 1, 1, -1) };
        var replacedSource = current with { Source = current.Source with { Lifetime = current.Source.Lifetime + 1 } };
        var changedInfluence = current with { InfluenceGeneration = current.InfluenceGeneration + 1 };
        var movedOutside = current with { PositionOrDirection = new Vector3d(8, 2.5, 2.5) };

        // Assert: depth-only projection changes do not rebuild candidates; association dependencies do.
        Assert.True(request.Covers(changedProjection));
        Assert.False(request.Covers(changedDomain));
        Assert.False(request.Covers(lostChanges));
        Assert.False(request.Covers(laggingChanges));
        Assert.False(request.Covers(largerReceiver));
        Assert.False(request.Covers(replacedSource));
        Assert.False(request.Covers(changedInfluence));
        Assert.False(request.Covers(movedOutside));
        Assert.False(request.NeedsRefresh(changedProjection, .25));
    }

    /// <summary>Exact box interpolation includes source and receiver overlap without extending beyond finite segments.</summary>
    [Fact]
    public void PointReferenceIncludesCoincidentAndOverlappingBoxes()
    {
        // Arrange: compare boxes that coincide and overlap through the exhaustive independent geometric reference.
        var coincident = new ShadowBounds(0.2, 0.2, 0.2, 0.8, 0.8, 0.8);
        var overlap = new ShadowBounds(0.5, 0.5, 0.5, 1.5, 1.5, 1.5);

        // Act: enumerate closed chunk-cell intersections with the convex receiver/source segment hull.
        var same = ShadowCandidateOracle.Point(Domain, coincident, coincident);
        var shared = ShadowCandidateOracle.Point(Domain, coincident, overlap);

        // Assert: coincident endpoints retain their own cell; overlapping boxes include boundary-adjacent touched cells.
        Assert.Equal(new HashSet<(int, int, int)> { (0, 0, 0) }, same);
        Assert.Contains((0, 0, 0), shared);
        Assert.Contains((1, 1, 1), shared);
        Assert.DoesNotContain((2, 2, 2), shared);
    }

    /// <summary>A fixed source coordinate remains valid without demanding an impossible interior refresh margin.</summary>
    [Fact]
    public void DegeneratePointCoverageDoesNotRefreshAtItsOnlyValidCoordinate()
    {
        // Arrange: anchor a stationary point source whose movement box has zero extent on every axis.
        var fixedBox = new ShadowBounds(2.5, 2.5, 2.5, 2.5, 2.5, 2.5);
        var request = ShadowCandidateRequest.Point(Source with { Kind = ShadowSourceKind.Entity },
            7, 3, 4, Domain, Receiver, fixedBox);
        var current = Current(request, Receiver, new Vector3d(2.5, 2.5, 2.5));

        // Act: query the refresh guard at the only valid source coordinate and just outside it.
        bool refreshAtAnchor = request.NeedsRefresh(current, .25);
        bool remainsCoveredAfterMotion = request.Covers(current with
            { PositionOrDirection = new Vector3d(2.5001, 2.5, 2.5) });

        // Assert: fixed coverage remains reusable at its exact anchor and expires on any movement beyond it.
        Assert.False(refreshAtAnchor);
        Assert.False(remainsCoveredAfterMotion);
    }

    /// <summary>Known finite inputs are required before directional or point coverage can be retained.</summary>
    [Fact]
    public void InvalidIdentityBoundsAndMotionAreRejected()
    {
        // Arrange: keep valid baseline request values and derive one invalid field at a time.
        var sourceBox = new ShadowBounds(2, 2, 2, 3, 3, 3);

        // Act and assert: unknown sources, dimension mismatch, unbounded source, and nonfinite motion fail closed.
        Assert.Throws<ArgumentException>(() => ShadowCandidateRequest.Directional(default, 7, 3, 4,
            Domain, Receiver, new Vector3d(1, 0, 0), 5));
        Assert.Throws<ArgumentException>(() => ShadowCandidateRequest.Point(Source with { Dimension = 1 }, 7, 3, 4,
            Domain, Receiver, sourceBox));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowCandidateRequest.Point(Source with { Kind = ShadowSourceKind.Entity },
            7, 3, 4, Domain, Receiver, new ShadowBounds(3_000_000, 3_000_000, 3_000_000,
                3_000_001, 3_000_001, 3_000_001)));
        var point = ShadowCandidateRequest.Point(Source with { Kind = ShadowSourceKind.Entity }, 7, 3, 4,
            Domain, Receiver, sourceBox);
        Assert.False(point.Covers(Current(point, Receiver, new Vector3d(double.NaN, 2.5, 2.5))));
    }

    /// <summary>Bounded relative coordinates preserve translated origins near both signed integer limits.</summary>
    [Fact]
    public void LargeSignedOriginsRemainRepresentableWithinDeclaredDomain()
    {
        // Arrange: keep each local domain small while translating it near both int32 coordinate extremes.
        int positive = int.MaxValue - 8, negative = int.MinValue + 8;
        var positiveDomain = new ShadowChunkDomain(0, positive, 0, 0, positive + 3, 2, 2);
        var negativeDomain = new ShadowChunkDomain(0, negative, 0, 0, negative + 3, 2, 2);
        var positiveReceiver = new ShadowBounds(positive + 0.25, 0.25, 0.25, positive + 0.75, 0.75, 0.75);
        var negativeReceiver = new ShadowBounds(negative + 0.25, 0.25, 0.25, negative + 0.75, 0.75, 0.75);

        // Act: create valid directional coverage using each local origin.
        var positiveRequest = ShadowCandidateRequest.Directional(Source, 7, 3, 4, positiveDomain,
            positiveReceiver, new Vector3d(-1, 0, 0), 3);
        var negativeRequest = ShadowCandidateRequest.Directional(Source, 7, 3, 4, negativeDomain,
            negativeReceiver, new Vector3d(1, 0, 0), 3);

        // Assert: translation near either limit does not change local validity or association identity rules.
        Assert.True(positiveDomain.IsValid);
        Assert.True(negativeDomain.IsValid);
        Assert.True(positiveRequest.Covers(Current(positiveRequest, positiveReceiver, new Vector3d(-1, 0, 0))));
        Assert.True(negativeRequest.Covers(Current(negativeRequest, negativeReceiver, new Vector3d(1, 0, 0))));
    }

    /// <summary>Domains wider than the bounded relative arithmetic ceiling are rejected instead of truncated.</summary>
    [Fact]
    public void UnrepresentableDomainSpanIsRejected()
    {
        // Arrange: declare a loaded axis wider than the contract's bounded one-million-cell extent.
        var tooWide = new ShadowChunkDomain(0, 0, 0, 0, 1_048_577, 0, 0);

        // Act and assert: construction cannot produce a partial domain or shortened association.
        Assert.False(tooWide.IsValid);
        Assert.Throws<ArgumentException>(() => ShadowCandidateRequest.Directional(Source, 7, 3, 4,
            tooWide, Receiver, new Vector3d(1, 0, 0), 5));
    }

    /// <summary>Every sampled direction in each cap is enclosed by one signed-axis slab sector.</summary>
    [Theory]
    [InlineData(1, 0, 0, 5)]
    [InlineData(-1, 0, 0, 5)]
    [InlineData(0, 1, 0, 5)]
    [InlineData(0, -1, 0, 5)]
    [InlineData(0, 0, 1, 5)]
    [InlineData(0, 0, -1, 5)]
    [InlineData(1, 2, 3, 18)]
    [InlineData(-1, 1, 0, 35)]
    [InlineData(0.001, -1, 0.002, 80)]
    [InlineData(1, 1, 1, 125)]
    [InlineData(1, 0.000000000001, 0, 4)]
    public void DirectionalSectorsEncloseCapInterior(double x, double y, double z, double radius)
    {
        // Arrange: sample a deterministic latitude-longitude lattice across the complete spherical cap.
        var request = ShadowCandidateRequest.Directional(Source, 7, 3, 4, Domain, Receiver,
            new Vector3d(x, y, z), radius);
        var center = Normalize(new Vector3d(x, y, z));
        var tangent = Normalize(Math.Abs(center.Y) < .8
            ? Cross(center, new Vector3d(0, 1, 0))
            : Cross(center, new Vector3d(1, 0, 0)));
        var bitangent = Cross(center, tangent);

        // Act: generate interior and boundary directions, then locate their axial sector and slopes.
        var missed = new List<Vector3d>();
        for (int ring = 0; ring <= 40; ring++)
        for (int spoke = 0; spoke < 96; spoke++)
        {
            double angle = radius * Math.PI / 180 * ring / 40.0;
            double azimuth = 2 * Math.PI * spoke / 96.0;
            var radial = tangent * Math.Cos(azimuth) + bitangent * Math.Sin(azimuth);
            var d = center * Math.Cos(angle) + radial * Math.Sin(angle);
            if (!CoveredByAnySector(request.Sectors, d)) missed.Add(d);
        }

        // Assert: cap interior and sector-boundary samples all remain in the conservative slab bounds.
        Assert.Empty(missed);
        Assert.InRange(request.Sectors.Length, 1, 6);
    }
    #endregion

    #region Private
    /// <summary>Builds current dependencies without coupling the test to projection generation semantics.</summary>
    private static ShadowCandidateCurrent Current(ShadowCandidateRequest request, ShadowBounds receiver,
        Vector3d position, long projection = 1) => new(request.Source, request.WorldOwner, request.DomainGeneration,
            request.InfluenceGeneration, receiver, position, request.ChangeEpoch, request.ChangeSequence, projection);

    /// <summary>Checks whether one signed sweep sector's axial and transverse bounds contain a direction.</summary>
    private static bool CoveredByAnySector(IReadOnlyList<ShadowCandidateSector> sectors, Vector3d direction)
    {
        foreach (var sector in sectors)
        {
            double[] values = [direction.X, direction.Y, direction.Z];
            double axial = sector.Sign * values[sector.Axis];
            if (axial <= 0) continue;
            int a = (sector.Axis + 1) % 3, b = (sector.Axis + 2) % 3;
            double slopeA = values[a] / axial, slopeB = values[b] / axial;
            if (slopeA >= sector.MinA && slopeA <= sector.MaxA
                && slopeB >= sector.MinB && slopeB <= sector.MaxB) return true;
        }
        return false;
    }

    /// <summary>Rotates a vector through a known angular offset for cap and guard boundary cases.</summary>
    private static Vector3d Rotate(Vector3d vector, double degrees, Vector3d axis)
    {
        var v = Normalize(vector); var a = Normalize(axis);
        double angle = degrees * Math.PI / 180;
        return Normalize(v * Math.Cos(angle) + Cross(a, v) * Math.Sin(angle)
            + a * (Dot(a, v) * (1 - Math.Cos(angle))));
    }

    /// <summary>Normalizes deterministic test vectors.</summary>
    private static Vector3d Normalize(Vector3d value)
    {
        double length = Math.Sqrt(Dot(value, value));
        return new(value.X / length, value.Y / length, value.Z / length);
    }

    /// <summary>Computes a three-dimensional cross product for deterministic cap sampling.</summary>
    private static Vector3d Cross(Vector3d a, Vector3d b) => new(
        a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Computes a three-dimensional dot product.</summary>
    private static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    #endregion
}
