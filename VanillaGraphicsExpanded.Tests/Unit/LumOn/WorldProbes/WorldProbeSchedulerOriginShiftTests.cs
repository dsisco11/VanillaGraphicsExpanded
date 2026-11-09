using System;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks origin snapping, ring-buffer shifts, and probe storage preservation in the scheduler.</summary>
public sealed class WorldProbeSchedulerOriginShiftTests
{
    #region Public API
    #region Anchor movement and events
    /// <summary>Verifies that moving within the same anchor cell preserves the origin and ring offset.</summary>
    [Fact]
    public void UpdateOrigins_DoesNotShiftWithinSameAnchorCell()
    {
        // Arrange
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 8);
        const double baseSpacing = 4.0;
        scheduler.UpdateOrigins(new Vec3d(1.0, 2.0, 3.0), baseSpacing);
        // Assert
        Assert.True(scheduler.TryGetLevelParams(0, out var origin0, out var ring0));

        // Still within the same snapped cell for spacing=4.
        // Act and Assert
        scheduler.UpdateOrigins(new Vec3d(3.99, 2.0, 3.0), baseSpacing);
        Assert.True(scheduler.TryGetLevelParams(0, out var origin1, out var ring1));

        Assert.Equal(origin0.X, origin1.X, 10);
        Assert.Equal(origin0.Y, origin1.Y, 10);
        Assert.Equal(origin0.Z, origin1.Z, 10);

        Assert.Equal(ring0.X, ring1.X);
        Assert.Equal(ring0.Y, ring1.Y);
        Assert.Equal(ring0.Z, ring1.Z);
    }

    /// <summary>Verifies that crossing the shift threshold advances the origin and wraps the ring offset.</summary>
    [Fact]
    public void UpdateOrigins_ShiftsAfterTwoWorldVoxels_UpdatesRingOffsetAndOrigin()
    {
        // Arrange
        const int resolution = 8;
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);
        const double baseSpacing = 4.0;
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);
        Assert.True(scheduler.TryGetLevelParams(0, out var origin0, out var ring0));

        // Act
        scheduler.UpdateOrigins(new Vec3d(4.01, 0.0, 0.0), baseSpacing);
        // Assert
        Assert.True(scheduler.TryGetLevelParams(0, out var origin1, out var ring1));

        // One-probe shift in +X means the ring offset shifts by +1 (wrapped).
        Assert.Equal((ring0.X + 1) % resolution, ring1.X);
        Assert.Equal(ring0.Y, ring1.Y);
        Assert.Equal(ring0.Z, ring1.Z);

        // Anchor moved by +spacing, so origin min-corner moves by +spacing as well.
        Assert.Equal(origin0.X + baseSpacing, origin1.X, 10);
    }

    /// <summary>Verifies that each anchor shift reports its level, delta, snapped anchor, origin, and ring offsets.</summary>
    [Fact]
    public void UpdateOrigins_ShiftEmitsAnchorShiftedEvent()
    {
        // Arrange
        const int resolution = 8;
        const double baseSpacing = 4.0;

        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);

        var events = new List<LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>();
        scheduler.AnchorShifted += e => events.Add(e);

        // First-time init should not fire.
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);
        Assert.Empty(events);
        Assert.True(scheduler.TryGetLevelParams(0, out var origin0, out var ring0));

        // Shift by +1 probe cell in X after crossing the grid boundary.
        // Act
        scheduler.UpdateOrigins(new Vec3d(4.01, 0.0, 0.0), baseSpacing);

        // Assert
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

    /// <summary>Verifies that movement near a block boundary triggers shifts only after crossing the deadband.</summary>
    [Fact]
    public void UpdateOrigins_DoesNotOscillateAcrossAdjacentBlockBoundary()
    {
        // Arrange
        const double baseSpacing = 4.0;
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution: 8);
        var events = new List<LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>();
        scheduler.AnchorShifted += e => events.Add(e);
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);

        // Crossing the block boundary at X=4 repeatedly must not reanchor the clipmap.
        // Act
        scheduler.UpdateOrigins(new Vec3d(3.99, 0.0, 0.0), baseSpacing);
        scheduler.UpdateOrigins(new Vec3d(4.01, 0.0, 0.0), baseSpacing);
        scheduler.UpdateOrigins(new Vec3d(3.99, 0.0, 0.0), baseSpacing);
        scheduler.UpdateOrigins(new Vec3d(4.01, 0.0, 0.0), baseSpacing);
        // Assert
        Assert.Single(events);
        Assert.Equal(new Vec3i(1, 0, 0), events[0].DeltaProbes);

        // The fixed two-voxel deadband prevents any subsequent reanchor near the boundary.
        // Act and Assert
        scheduler.UpdateOrigins(new Vec3d(2.01, 0.0, 0.0), baseSpacing);
        Assert.Single(events);

        // Moving more than two base-game voxels from the new anchor shifts back once.
        // Act and Assert
        scheduler.UpdateOrigins(new Vec3d(1.99, 0.0, 0.0), baseSpacing);
        Assert.Equal(2, events.Count);
        Assert.Equal(new Vec3i(-1, 0, 0), events[1].DeltaProbes);
    }

    /// <summary>Verifies that every clipmap level uses the two-base-voxel hysteresis threshold.</summary>
    [Fact]
    public void UpdateOrigins_UsesTwoBaseVoxelsForEveryClipmapLevel()
    {
        // Arrange
        const double baseSpacing = 4.0;
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 2, resolution: 8);
        var events = new List<LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>();
        scheduler.AnchorShifted += e => events.Add(e);
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);
        // Act
        scheduler.UpdateOrigins(new Vec3d(8.01, 0.0, 0.0), baseSpacing);
        // Assert
        Assert.Contains(events, e => e.Level == 1 && e.DeltaProbes == new Vec3i(1, 0, 0));

        events.Clear();

        // L1 spacing is 8 voxels. Returning by 2.01 voxels must still reanchor.
        // Act and Assert
        scheduler.UpdateOrigins(new Vec3d(5.99, 0.0, 0.0), baseSpacing);
        Assert.Contains(events, e => e.Level == 1 && e.DeltaProbes == new Vec3i(-1, 0, 0));
    }
    #endregion

    #region Ring storage and invalidation
    /// <summary>Verifies that ring shifts preserve storage values for every probe in the overlapping volume.</summary>
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
        // Arrange
        const int resolution = 8;
        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);
        const double baseSpacing = 4.0;

        // Initial state at ring=0.
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);
        // Assert
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

        // Shift anchor by the requested delta after moving beyond the two-voxel deadband.
        static double HystereticCoordinate(int delta, double spacing) => delta switch
        {
            > 0 => (delta * spacing) + 0.01,
            < 0 => (delta * spacing) + 0.01,
            _ => 0.0
        };
        // Act and Assert
        scheduler.UpdateOrigins(
            new Vec3d(
                HystereticCoordinate(dx, baseSpacing),
                HystereticCoordinate(dy, baseSpacing),
                HystereticCoordinate(dz, baseSpacing)),
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

    /// <summary>Verifies that invalidating in-flight probes leaves them dirty after successful completion.</summary>
    [Fact]
    public void MarkDirtyWorldAabb_WhenProbeInFlight_MarksDirtyAfterComplete()
    {
        // Arrange
        const int resolution = 4;
        const double baseSpacing = 2.0;

        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);

        // Initialize params so origin exists.
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);

        int probesPerLevel = scheduler.ProbesPerLevel;
        int[] perLevelBudgets = [probesPerLevel];

        // Force everything into InFlight by selecting and then claiming all probes.
        var requests = scheduler.BuildUpdateList(
            frameIndex: 1,
            cameraPos: new Vec3d(0.0, 0.0, 0.0),
            baseSpacing: baseSpacing,
            perLevelProbeBudgets: perLevelBudgets,
            traceMaxProbesPerFrame: probesPerLevel,
            uploadBudgetBytesPerFrame: int.MaxValue,
            atlasTexelsPerUpdate: 32);
        Assert.Equal(probesPerLevel, requests.Count);

        for (int i = 0; i < requests.Count; i++)
        {
            Assert.True(scheduler.TryClaim(requests[i], frameIndex: 1));
        }

        // Invalidate the whole clip volume while probes are in-flight.
        Assert.True(scheduler.TryGetLevelParams(0, out var originMin, out _));
        double size = baseSpacing * resolution;
        // Act
        scheduler.MarkDirtyWorldAabb(0, originMin, new Vec3d(originMin.X + size, originMin.Y + size, originMin.Z + size), baseSpacing);

        // Completing an in-flight probe should yield Dirty, not Valid.
        var req = requests[0];
        scheduler.Complete(req, frameIndex: 2, success: true);

        // Assert
        var states = new LumOnWorldProbeLifecycleState[probesPerLevel];
        Assert.True(scheduler.TryCopyLifecycleStates(0, states));
        Assert.Equal(LumOnWorldProbeLifecycleState.Dirty, states[req.StorageLinearIndex]);
    }

    /// <summary>Verifies that a shifted slab is re-enabled as dirty even when its storage cell was disabled.</summary>
    [Fact]
    public void UpdateOrigins_ShiftMarksIntroducedSlabsDirty_EvenIfPreviouslyDisabled()
    {
        // Arrange
        const int resolution = 4;
        const double baseSpacing = 4.0;

        var scheduler = new LumOnWorldProbeScheduler(levelCount: 1, resolution);
        scheduler.UpdateOrigins(new Vec3d(0.0, 0.0, 0.0), baseSpacing);

        // Disable a storage cell that will be re-used by the introduced X slab after a +X shift.
        // For dx=+1, introduced local slab is x=resolution-1, which maps to storage x=0 (because ringOffset.X becomes 1).
        var disabledStorage = new Vec3i(0, 0, 0);
        int disabledLinear = Linear(disabledStorage, resolution);
        Assert.True(scheduler.UpdateImportanceFlags(
            level: 0,
            storageLinearIndex: disabledLinear,
            setFlags: LumOnWorldProbeImportanceFlags.NearbySolidHit,
            clearFlags: LumOnWorldProbeImportanceFlags.None));
        scheduler.Disable(new LumOnWorldProbeUpdateRequest(
            Level: 0,
            LocalIndex: new Vec3i(0, 0, 0),
            StorageIndex: disabledStorage,
            StorageLinearIndex: disabledLinear));

        // Shift by +1 probe cell in X after crossing the grid boundary.
        // Act
        scheduler.UpdateOrigins(new Vec3d(baseSpacing + 0.01, 0.0, 0.0), baseSpacing);

        // Assert
        var states = new LumOnWorldProbeLifecycleState[scheduler.ProbesPerLevel];
        Assert.True(scheduler.TryCopyLifecycleStates(0, states));

        Assert.Equal(LumOnWorldProbeLifecycleState.Dirty, states[disabledLinear]);
        Assert.True(scheduler.TryGetImportanceFlags(0, disabledLinear, out var flags));
        Assert.Equal(LumOnWorldProbeImportanceFlags.None, flags);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Returns a nonnegative remainder for wrapping one index into ring storage.</summary>
    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    /// <summary>Wraps each coordinate of a vector into the ring storage range.</summary>
    private static Vec3i Wrap(Vec3i v, int n) => new(
        Wrap(v.X, n),
        Wrap(v.Y, n),
        Wrap(v.Z, n));

    /// <summary>Encodes a local clipmap coordinate as a linear probe index.</summary>
    private static int EncodeLocal(Vec3i local, int resolution) =>
        local.X + local.Y * resolution + local.Z * resolution * resolution;

    /// <summary>Encodes a ring storage coordinate as a linear index.</summary>
    private static int Linear(Vec3i v, int resolution) =>
        v.X + v.Y * resolution + v.Z * resolution * resolution;

    /// <summary>Reports whether all coordinates lie inside the clipmap extent.</summary>
    private static bool IsInBounds(Vec3i local, int resolution) =>
        (uint)local.X < (uint)resolution &&
        (uint)local.Y < (uint)resolution &&
        (uint)local.Z < (uint)resolution;
    #endregion
}
