using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class LumOnClipmapTopologyTests
{
    [Theory]
    [InlineData(0.0, 1.7, 0.0)]
    [InlineData(0.99, 3.01, 0.99)]
    [InlineData(-0.01, 3.01, -0.01)]
    [InlineData(12.34, 64.0, -98.76)]
    [InlineData(-12.34, -64.0, 98.76)]
    public void UpdateOrigins_OriginMatchesSnappedAnchor(double camX, double camY, double camZ)
    {
        const int resolution = 8;
        const double baseSpacing = 2.0;

        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);
        scheduler.UpdateOrigins(new Vec3d(camX, camY, camZ), baseSpacing);

        Assert.True(scheduler.TryGetLevelParams(0, out var origin, out _));

        var cam = new Vec3d(camX, camY, camZ);
        var anchor = LumOnClipmapTopology.SnapAnchor(cam, baseSpacing);
        var expected = LumOnClipmapTopology.GetOriginMinCorner(anchor, baseSpacing, resolution);

        Assert.Equal(expected.X, origin.X, 10);
        Assert.Equal(expected.Y, origin.Y, 10);
        Assert.Equal(expected.Z, origin.Z, 10);
    }

    [Theory]
    [InlineData(0.0, 1.7, 0.0)]
    [InlineData(1.99, 3.99, 1.99)]
    [InlineData(-0.01, 3.01, -0.01)]
    [InlineData(-1.99, -3.01, -1.99)]
    public void UpdateOrigins_CameraLocalStaysNearClipmapCenter(double camX, double camY, double camZ)
    {
        const int resolution = 8;
        const double spacing = 2.0;

        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);
        scheduler.UpdateOrigins(new Vec3d(camX, camY, camZ), spacing);

        Assert.True(scheduler.TryGetLevelParams(0, out var origin, out _));

        Vec3d local = LumOnClipmapTopology.WorldToLocal(new Vec3d(camX, camY, camZ), origin, spacing);

        // With floor-snapped anchors, the camera lies within a single grid cell of the anchor.
        // Therefore its local coordinate is always within [resolution/2, resolution/2 + 1) on each axis.
        double half = resolution * 0.5;
        Assert.InRange(local.X, half, half + 1.0);
        Assert.InRange(local.Y, half, half + 1.0);
        Assert.InRange(local.Z, half, half + 1.0);
    }

    [Fact]
    public void IndexToProbeCenterWorld_FirstAndLastCentersAreInsideOuterBounds()
    {
        const int resolution = 8;
        const double spacing = 2.0;

        var cam = new Vec3d(0.0, 0.0, 0.0);
        var anchor = LumOnClipmapTopology.SnapAnchor(cam, spacing);
        var origin = LumOnClipmapTopology.GetOriginMinCorner(anchor, spacing, resolution);

        double size = spacing * resolution;

        Vec3d first = LumOnClipmapTopology.IndexToProbeCenterWorld(new Vec3i(0, 0, 0), origin, spacing);
        Vec3d last = LumOnClipmapTopology.IndexToProbeCenterWorld(new Vec3i(resolution - 1, resolution - 1, resolution - 1), origin, spacing);

        // Centers are offset by half a cell from the outer bounds.
        double inset = spacing * 0.5;
        Assert.Equal(origin.X + inset, first.X, 10);
        Assert.Equal(origin.Y + inset, first.Y, 10);
        Assert.Equal(origin.Z + inset, first.Z, 10);

        Assert.Equal(origin.X + size - inset, last.X, 10);
        Assert.Equal(origin.Y + size - inset, last.Y, 10);
        Assert.Equal(origin.Z + size - inset, last.Z, 10);

        Assert.InRange(first.X, origin.X, origin.X + size);
        Assert.InRange(first.Y, origin.Y, origin.Y + size);
        Assert.InRange(first.Z, origin.Z, origin.Z + size);

        Assert.InRange(last.X, origin.X, origin.X + size);
        Assert.InRange(last.Y, origin.Y, origin.Y + size);
        Assert.InRange(last.Z, origin.Z, origin.Z + size);
    }
}

