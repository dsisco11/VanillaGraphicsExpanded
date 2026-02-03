using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneRegionCellHeatDesiredStateTests
{
    [Fact]
    public void Heat_PromotesLoadedToActive_OutsideActiveWindow()
    {
        var cell = new LumonSceneRegionCell(
            kind: WorldCellKind.LumonSceneNear,
            chunkCoord: new LumonSceneChunkCoord(3, 0, 3));

        var ctx = new WorldCellStateTransitionContext(
            CameraBlockPos: default,
            AnchorBlockPos: default,
            HasAnchor: false,
            LoadedWindowMinRegion: new VectorInt3(0, 0, 0),
            LoadedWindowMaxRegion: new VectorInt3(4, 4, 4),
            HasLoadedWindow: true,
            ActiveWindowMinRegion: new VectorInt3(0, 0, 0),
            ActiveWindowMaxRegion: new VectorInt3(2, 2, 2),
            HasActiveWindow: true,
            NowTick: 100);

        Assert.Equal(WorldCellDesiredState.Loaded, cell.CalculateDesiredState(in ctx));

        cell.ApplyHeatFromRequests(requestCount: 1, nowTick: 100);
        Assert.Equal(WorldCellDesiredState.Active, cell.CalculateDesiredState(in ctx));
    }
}
