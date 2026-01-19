using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeSchedulerOriginShiftTests
{
    [Fact]
    public void UpdateOrigins_DoesNotShiftWithinSameAnchorCell()
    {
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 8);
        const double baseSpacing = 4.0;

        scheduler.UpdateOrigins(new Vec3d(1.0, 2.0, 3.0), baseSpacing);
        Assert.True(scheduler.TryGetLevelParams(0, out var origin0, out var ring0));

        // Still within the same snapped cell for spacing=4.
        scheduler.UpdateOrigins(new Vec3d(3.99, 2.0, 3.0), baseSpacing);
        Assert.True(scheduler.TryGetLevelParams(0, out var origin1, out var ring1));

        Assert.Equal(origin0.X, origin1.X, 10);
        Assert.Equal(origin0.Y, origin1.Y, 10);
        Assert.Equal(origin0.Z, origin1.Z, 10);

        Assert.Equal(ring0.X, ring1.X);
        Assert.Equal(ring0.Y, ring1.Y);
        Assert.Equal(ring0.Z, ring1.Z);
    }

    [Fact]
    public void UpdateOrigins_ShiftsAcrossBoundary_UpdatesRingOffsetAndOrigin()
    {
        const int resolution = 8;
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);
        const double baseSpacing = 4.0;

        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);
        Assert.True(scheduler.TryGetLevelParams(0, out var origin0, out var ring0));

        scheduler.UpdateOrigins(new Vec3d(4.01, 0.0, 0.0), baseSpacing);
        Assert.True(scheduler.TryGetLevelParams(0, out var origin1, out var ring1));

        // One-probe shift in +X means the ring offset shifts by -1 (wrapped).
        Assert.Equal((ring0.X - 1 + resolution) % resolution, ring1.X);
        Assert.Equal(ring0.Y, ring1.Y);
        Assert.Equal(ring0.Z, ring1.Z);

        // Anchor moved by +spacing, so origin min-corner moves by +spacing as well.
        Assert.Equal(origin0.X + baseSpacing, origin1.X, 10);
    }
}
