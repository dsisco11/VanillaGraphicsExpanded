using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldCells;

public sealed class WorldCellWindowHysteresisTests
{
    [Fact]
    public void LeavingActiveWindow_HoldsActiveForNTicks()
    {
        long holdUntil = 0;

        var loadedMin = new VectorInt3(-10, -10, -10);
        var loadedMax = new VectorInt3(10, 10, 10);

        // Active window is a tight cube around the origin.
        var activeMin = new VectorInt3(-1, -1, -1);
        var activeMax = new VectorInt3(1, 1, 1);

        VectorInt3 cell = new VectorInt3(0, 0, 0);

        var ctx0 = new WorldCellStateTransitionContext(
            CameraBlockPos: default,
            AnchorBlockPos: default,
            HasAnchor: false,
            LoadedWindowMinRegion: loadedMin,
            LoadedWindowMaxRegion: loadedMax,
            HasLoadedWindow: true,
            ActiveWindowMinRegion: activeMin,
            ActiveWindowMaxRegion: activeMax,
            HasActiveWindow: true,
            NowTick: 100);

        Assert.Equal(WorldCellDesiredState.Active,
            WorldCellWindowHysteresis.CalculateDesiredState(cell, in ctx0, ref holdUntil, activeHoldTicks: 5));

        // Move active window away, but keep loaded window containing the cell.
        var ctx1 = ctx0 with
        {
            ActiveWindowMinRegion = new VectorInt3(100, 100, 100),
            ActiveWindowMaxRegion = new VectorInt3(101, 101, 101),
            NowTick = 101,
        };

        Assert.Equal(WorldCellDesiredState.Active,
            WorldCellWindowHysteresis.CalculateDesiredState(cell, in ctx1, ref holdUntil, activeHoldTicks: 5));

        // After the hold expires, it should downgrade to Loaded.
        var ctx2 = ctx1 with { NowTick = 200 };

        Assert.Equal(WorldCellDesiredState.Loaded,
            WorldCellWindowHysteresis.CalculateDesiredState(cell, in ctx2, ref holdUntil, activeHoldTicks: 5));
    }

    [Fact]
    public void LeavingLoadedWindow_DowngradesToUnloaded()
    {
        long holdUntil = 0;

        VectorInt3 cell = new VectorInt3(0, 0, 0);

        var ctx = new WorldCellStateTransitionContext(
            CameraBlockPos: default,
            AnchorBlockPos: default,
            HasAnchor: false,
            LoadedWindowMinRegion: new VectorInt3(10, 10, 10),
            LoadedWindowMaxRegion: new VectorInt3(11, 11, 11),
            HasLoadedWindow: true,
            ActiveWindowMinRegion: default,
            ActiveWindowMaxRegion: default,
            HasActiveWindow: false,
            NowTick: 1);

        Assert.Equal(WorldCellDesiredState.Unloaded,
            WorldCellWindowHysteresis.CalculateDesiredState(cell, in ctx, ref holdUntil, activeHoldTicks: 10));
    }
}
