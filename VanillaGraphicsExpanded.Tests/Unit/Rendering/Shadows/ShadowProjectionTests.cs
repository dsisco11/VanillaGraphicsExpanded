using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects stable directional page mapping and the six point-face projection conventions.</summary>
public sealed class ShadowProjectionTests
{
    private static readonly ShadowSourceKey Sun = new(4, 2, ShadowSourceKind.Sun, 1, 3, 0);
    private static readonly ShadowSourceKey Moon = Sun with { Kind = ShadowSourceKind.Moon, Slot = 1 };
    private static readonly ShadowBounds Casters = new(-100, -30, -80, 120, 60, 90);

    #region Public API
    #region Directional Mapping
    /// <summary>Sun and moon own independent keys while clipmap levels double texel width.</summary>
    [Fact]
    public void CelestialSourcesAndClipmapLevelsAreIndependent()
    {
        // Arrange: give each celestial source the same basis and represented domain.
        var sun = new DirectionalShadowProjection(Sun, 7, new Vector3d(1, 2, 3), Casters);
        var moon = new DirectionalShadowProjection(Moon, 7, new Vector3d(1, 2, 3), Casters);
        var receiver = new Vector3d(-19, 8, 31);

        // Act: address that point at adjacent levels for both distinct source identities.
        var sunFine = sun.Key(receiver, 0);
        var sunCoarse = sun.Key(receiver, 1);
        var moonFine = moon.Key(receiver, 0);

        // Assert: level scale doubles, and equal projections never merge solar and lunar assignments.
        Assert.Equal(sun.TexelWidth(0) * 2, sun.TexelWidth(1));
        Assert.Equal(0, sunFine.FaceOrLevel);
        Assert.Equal(1, sunCoarse.FaceOrLevel);
        Assert.Equal(Sun, sunFine.Source);
        Assert.Equal(Moon, moonFine.Source);
        Assert.NotEqual(sunFine, moonFine);
    }

    /// <summary>Clipmap addresses use stable world coordinates and mathematical floor at negative page boundaries.</summary>
    [Fact]
    public void DirectionalPageKeysRemainWorldStableAtLargeAndNegativeCoordinates()
    {
        // Arrange: one axis-aligned projection makes the absolute page arithmetic directly predictable.
        var projection = new DirectionalShadowProjection(Sun, 11, new Vector3d(0, 0, 1),
            new ShadowBounds(-2, -2, -2, 2, 2, 2), baseTexelWidth: 1, levels: 3, pageSize: 128);
        var world = new Vector3d(-1_000_000, 2_000_000, 3_000_000);

        // Act: address a point, translate the camera by a small amount, and request aligned centers.
        var initial = projection.Key(world, 0);
        var translated = projection.Key(world, 0);
        var center = projection.SnapCenter(world, 0);
        var nextCenter = projection.SnapCenter(world + new Vector3d(1, 0, 0), 0);

        // Assert: absolute virtual identity is independent of camera placement and uses floor for negative values.
        Assert.Equal(initial, translated);
        Assert.Equal(-1_000_000 / 128 - 1, initial.X);
        Assert.Equal(center, nextCenter);
    }

    /// <summary>Directional depth encloses every corner in the represented caster envelope with explicit padding.</summary>
    [Fact]
    public void DirectionalDepthRangeFitsRepresentedCasterEnvelope()
    {
        // Arrange: fit an oblique light basis to the full supplied caster envelope.
        var projection = new DirectionalShadowProjection(Sun, 13, new Vector3d(1, 2, -4), Casters,
            depthPadding: 3);

        // Act: project the eight independently enumerated envelope corners.
        var depths = Enumerable.Range(0, 8).Select(i =>
        {
            var corner = new Vector3d((i & 1) == 0 ? Casters.MinX : Casters.MaxX,
                (i & 2) == 0 ? Casters.MinY : Casters.MaxY,
                (i & 4) == 0 ? Casters.MinZ : Casters.MaxZ);
            return projection.Project(corner, 0).Z;
        }).ToArray();

        // Assert: the full domain fits inside conventional normalized depth and leaves the requested margin.
        Assert.All(depths, depth => Assert.InRange(depth, 0, 1));
        Assert.True(depths.Min() > 0);
        Assert.True(depths.Max() < 1);
        Assert.True(projection.Far > projection.Near);
    }
    #endregion

    #region Point Face and Projection Mapping
    /// <summary>Point face ties are deterministic and all six face centers project to the texture center.</summary>
    [Fact]
    public void PointProjectionUsesSixStableFaceAxes()
    {
        // Arrange: choose a translated light position and directions along the fixed cube-face axes.
        var position = new Vector3d(1_000_000, -2_000_000, 3_000_000);
        var point = new PointShadowProjection(Sun with { Kind = ShadowSourceKind.Entity }, 17, position,
            near: 0.1, far: 40, faceResolution: 1024, pageSize: 128);
        Vector3d[] axes = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

        // Act: classify and project each axis-aligned receiver at an equal distance.
        var results = axes.Select((axis, face) => (Face: PointShadowProjection.Face(axis),
            Projection: point.Project(position + axis * 10, face))).ToArray();

        // Assert: each axis owns its documented face and maps to center with the conventional perspective depth.
        Assert.Equal(Enumerable.Range(0, 6), results.Select(x => x.Face));
        Assert.All(results, result =>
        {
            Assert.Equal(512, result.Projection.X);
            Assert.Equal(512, result.Projection.Y);
            Assert.InRange(result.Projection.Z, 0, 1);
        });
        Assert.Equal(0, PointShadowProjection.Face(new Vector3d(1, 1, 1)));
        Assert.Equal(3, PointShadowProjection.Face(new Vector3d(0, -1, -1)));
    }

    /// <summary>Cube-edge remapping uses the adjacent face's own reference depth for the same world sample.</summary>
    [Fact]
    public void PointFaceEdgeRemappingRecomputesDepthInDestinationFace()
    {
        // Arrange: construct one sample beyond the +Z face edge and choose the destination with the fixed tie rule.
        var position = new Vector3d(-17, 400, 1_200);
        var point = new PointShadowProjection(Sun with { Kind = ShadowSourceKind.Entity }, 19, position,
            near: 0.25, far: 100, faceResolution: 2048, pageSize: 128);
        var direction = PointShadowProjection.Direction(4, u: 1.2, v: -0.4);
        int adjacentFace = PointShadowProjection.Face(direction);
        var receiver = position + direction * 12;

        // Act: project the same receiver in the source and remapped destination faces.
        var sourceFace = point.Project(receiver, 4);
        var destinationFace = point.Project(receiver, adjacentFace);

        // Assert: each face uses its own major-axis distance for conventional perspective depth.
        Assert.Equal(0, adjacentFace);
        Assert.InRange(sourceFace.Z, 0, 1);
        Assert.InRange(destinationFace.Z, 0, 1);
        double destinationMajorDistance = Math.Abs(receiver.X - position.X);
        double expectedDestinationDepth = point.Far / (point.Far - point.Near)
            - point.Far * point.Near / ((point.Far - point.Near) * destinationMajorDistance);
        Assert.Equal(expectedDestinationDepth, destinationFace.Z, 12);
        Assert.NotEqual(sourceFace.Z, destinationFace.Z);
    }
    #endregion

    #region Identity Validation and Page Clip Mapping
    /// <summary>Invalid source identity, degenerate bases and unrepresentable page addresses fail explicitly.</summary>
    [Fact]
    public void ProjectionsRejectInvalidInputsAndIntegerAddressOverflow()
    {
        // Arrange: valid source identity and a directional mapping with a narrow finite texel width.
        var directional = new DirectionalShadowProjection(Sun, 23, new Vector3d(0, 0, 1),
            new ShadowBounds(-1, -1, -1, 1, 1, 1), baseTexelWidth: 0.01);
        var invalidSource = new ShadowSourceKey(0, 0, ShadowSourceKind.Unknown, 0, 0, 0);

        // Act and assert: undefined light bases, unknown ownership and int-overflow page keys are rejected.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DirectionalShadowProjection(Sun, 1,
            Vector3d.Zero, Casters));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DirectionalShadowProjection(invalidSource, 1,
            Vector3d.UnitZ, Casters));
        Assert.Throws<ArgumentOutOfRangeException>(() => directional.Key(new Vector3d(1e12, 0, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointShadowProjection(
            Sun with { Kind = ShadowSourceKind.Entity }, 1, Vector3d.Zero, near: 0, far: 10));
    }

    /// <summary>Projection updates retain generations for identical mappings and invalidate them after depth/light movement.</summary>
    [Fact]
    public void ProjectionUpdatesPreserveOnlyMatchingDepthMappings()
    {
        // Arrange: create a directional projection and a moving point source with fixed settings.
        var sun = new DirectionalShadowProjection(Sun, 31, Vector3d.UnitZ, Casters, baseTexelWidth: 1);
        var pointSource = Sun with { Kind = ShadowSourceKind.Hand, Slot = 2 };
        var point = new PointShadowProjection(pointSource, 41, new Vector3d(8, 9, 10),
            near: 0.05, far: 25, faceResolution: 1024, pageSize: 128);
        var settings = new ShadowPageSettings { PageSize = 128, PointNear = 0.05 };

        // Act: update without changes, then rotate the celestial basis and move the point source.
        var sameSun = sun.Update(Vector3d.UnitZ, Casters);
        var rotatedSun = sun.Update(new Vector3d(0.999, 0.001, 0), Casters);
        var samePoint = point.Update(point.Position, point.Far, point.FaceResolution, settings);
        var movedPoint = point.Update(point.Position + Vector3d.UnitX, point.Far, point.FaceResolution, settings);

        // Assert: translation reuse applies only to unchanged mapping; changed depth bases receive new identities.
        Assert.Same(sun, sameSun);
        Assert.NotSame(sun, rotatedSun);
        Assert.Equal(sun.Generation + 1, rotatedSun.Generation);
        Assert.Same(point, samePoint);
        Assert.NotSame(point, movedPoint);
        Assert.Equal(point.Generation + 1, movedPoint.Generation);
        Assert.NotEqual(point.Key(new Vector3d(10, 9, 10)), movedPoint.Key(new Vector3d(10, 9, 10)));
    }

    /// <summary>Page-local projection keeps its keyed virtual tile inside normal clip coordinates.</summary>
    [Fact]
    public void ProjectionMapsWorldPointsIntoPageLocalClipCoordinates()
    {
        // Arrange: choose a point well inside a directional page and its own projection key.
        var projection = new DirectionalShadowProjection(Sun, 43, Vector3d.UnitZ,
            new ShadowBounds(-512, -512, -512, 512, 512, 512), baseTexelWidth: 1);
        var world = new Vector3d(12.25, -17.5, 40);
        var key = projection.Key(world, 0);

        // Act: convert through the page's normal clip volume and reject a key from another generation.
        var clip = projection.ProjectPage(world, key);

        // Assert: page coordinates span the expected clip range and stale identities cannot be projected.
        Assert.InRange(clip.X, -1, 1);
        Assert.InRange(clip.Y, -1, 1);
        Assert.InRange(clip.Z, -1, 1);
        Assert.Throws<ArgumentException>(() => projection.ProjectPage(world, key with { Projection = key.Projection + 1 }));
    }
    #endregion
    #endregion
}
