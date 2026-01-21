using System;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeSchedulerOriginShiftTests
{
    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    private static Vec3i Wrap(Vec3i v, int n) => new(
        Wrap(v.X, n),
        Wrap(v.Y, n),
        Wrap(v.Z, n));

    private static int EncodeLocal(Vec3i local, int resolution) =>
        local.X + local.Y * resolution + local.Z * resolution * resolution;

    private static int Linear(Vec3i v, int resolution) =>
        v.X + v.Y * resolution + v.Z * resolution * resolution;

    private static bool IsInBounds(Vec3i local, int resolution) =>
        (uint)local.X < (uint)resolution &&
        (uint)local.Y < (uint)resolution &&
        (uint)local.Z < (uint)resolution;

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

        // One-probe shift in +X means the ring offset shifts by +1 (wrapped).
        Assert.Equal((ring0.X + 1) % resolution, ring1.X);
        Assert.Equal(ring0.Y, ring1.Y);
        Assert.Equal(ring0.Z, ring1.Z);

        // Anchor moved by +spacing, so origin min-corner moves by +spacing as well.
        Assert.Equal(origin0.X + baseSpacing, origin1.X, 10);
    }

    [Fact]
    public void UpdateOrigins_ShiftEmitsAnchorShiftedEvent()
    {
        const int resolution = 8;
        const double baseSpacing = 4.0;

        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);

        var events = new List<LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>();
        scheduler.AnchorShifted += e => events.Add(e);

        // First-time init should not fire.
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);
        Assert.Empty(events);

        Assert.True(scheduler.TryGetLevelParams(0, out var origin0, out var ring0));

        // Shift by +1 probe cell in X.
        scheduler.UpdateOrigins(new Vec3d(4.01, 0.0, 0.0), baseSpacing);

        Assert.Single(events);
        var ev = events[0];

        Assert.Equal(0, ev.Level);
        Assert.Equal(new Vec3i(1, 0, 0), ev.DeltaProbes);
        Assert.Equal(baseSpacing, ev.Spacing, 10);

        // Anchors are snapped.
        Assert.Equal(0.0, ev.PrevAnchor.X, 10);
        Assert.Equal(0.0, ev.PrevAnchor.Y, 10);
        Assert.Equal(0.0, ev.PrevAnchor.Z, 10);
        Assert.Equal(4.0, ev.NewAnchor.X, 10);

        // Origin min-corner changes by +spacing in X.
        Assert.Equal(origin0.X, ev.PrevOriginMinCorner.X, 10);
        Assert.Equal(origin0.Y, ev.PrevOriginMinCorner.Y, 10);
        Assert.Equal(origin0.Z, ev.PrevOriginMinCorner.Z, 10);
        Assert.Equal(origin0.X + baseSpacing, ev.NewOriginMinCorner.X, 10);

        // Ring offset matches scheduler's update.
        Assert.Equal(ring0, ev.PrevRingOffset);
        Assert.True(scheduler.TryGetLevelParams(0, out _, out var ring1));
        Assert.Equal(ring1, ev.NewRingOffset);
    }

    [Theory]
    [InlineData(+1, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 0, +1)]
    [InlineData(0, 0, -1)]
    [InlineData(+2, 0, 0)]
    [InlineData(+1, 0, +1)]
    [InlineData(-1, 0, +1)]
    [InlineData(+1, +1, 0)]
    public void UpdateOrigins_ShiftPreservesOverlappingStorageMapping(int dx, int dy, int dz)
    {
        const int resolution = 8;
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);
        const double baseSpacing = 4.0;

        // Initial state at ring=0.
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);
        Assert.True(scheduler.TryGetLevelParams(0, out _, out var ring0));

        // Simulate "probe data stored in ring buffer storage coords".
        // At time0, store the local index identity into the storage location where it lives.
        int[] storageToLocalId = new int[resolution * resolution * resolution];
        Array.Fill(storageToLocalId, -1);

        for (int z = 0; z < resolution; z++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    var local = new Vec3i(x, y, z);
                    var storage = Wrap(new Vec3i(local.X + ring0.X, local.Y + ring0.Y, local.Z + ring0.Z), resolution);
                    storageToLocalId[Linear(storage, resolution)] = EncodeLocal(local, resolution);
                }
            }
        }

        // Shift anchor by requested delta (cross spacing boundary). Use a small positive epsilon to stay inside the cell.
        // This keeps SnapAnchor() deterministic for both positive and negative deltas:
        //  - dx=-1 => camX = -4 + 0.01 => floor(-0.9975) => -1
        //  - dx=+1 => camX = +4 + 0.01 => floor(+1.0025) => +1
        static double Eps(int d) => d == 0 ? 0.0 : 0.01;
        scheduler.UpdateOrigins(
            new Vec3d(dx * baseSpacing + Eps(dx), dy * baseSpacing + Eps(dy), dz * baseSpacing + Eps(dz)),
            baseSpacing);
        Assert.True(scheduler.TryGetLevelParams(0, out _, out var ring1));

        // Overlap region invariant:
        // For a fixed world-space point, localNew = localOld - delta, so localOld = localNew + delta.
        // Any overlapping cell must read back the old local-id from the same storage coord after ring shift.
        for (int z = 0; z < resolution; z++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int xNew = 0; xNew < resolution; xNew++)
                {
                    var localNew = new Vec3i(xNew, y, z);
                    var storageNew = Wrap(new Vec3i(localNew.X + ring1.X, localNew.Y + ring1.Y, localNew.Z + ring1.Z), resolution);

                    var expectedOldLocal = new Vec3i(xNew + dx, y + dy, z + dz);
                    if (!IsInBounds(expectedOldLocal, resolution))
                    {
                        // Newly introduced slab(s); no overlap data to preserve.
                        continue;
                    }

                    int observedOldLocalId = storageToLocalId[Linear(storageNew, resolution)];
                    int expectedOldLocalId = EncodeLocal(expectedOldLocal, resolution);
                    Assert.Equal(expectedOldLocalId, observedOldLocalId);
                }
            }
        }
    }
}
