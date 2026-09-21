using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.WorldPartition;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneRegionSchedulerIntegrationSmokeTests
{
    [Fact]
    public void ActiveCells_EnqueueCaptureAndRelight_LoadedOnlyDoNot()
    {
        var scheduler = new LumonSceneRegionScheduler(new PartitionCoordinator(new(16384, 256, 128, 128, 33554432)));
        scheduler.Reset(nowTick: 0);

        var activeCell = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new LumonSceneChunkCoord(0, 0, 0));
        var loadedCell = scheduler.GetOrCreate(WorldCellKind.LumonSceneNear, new LumonSceneChunkCoord(1, 0, 0));

        activeCell.UpdateSlotAssignment(chunkSlot: 0, slotGeneration: 1, nowTick: 0);
        loadedCell.UpdateSlotAssignment(chunkSlot: 1, slotGeneration: 1, nowTick: 0);

        // Create a 2-slot page table mirror with one NeedsCapture + one NeedsRelight entry per slot.
        int vpc = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
        var mirror = new LumonScenePageTableEntry[vpc * 2];

        mirror[0 * vpc + 0] = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsCapture);

        mirror[0 * vpc + 1] = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 2,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

        mirror[1 * vpc + 0] = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 3,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsCapture);

        mirror[1 * vpc + 1] = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 4,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

        Assert.True(activeCell.TryUpdateBacklogFromPageTableMirror(mirror));
        Assert.True(loadedCell.TryUpdateBacklogFromPageTableMirror(mirror));

        activeCell.DesiredState = WorldCellDesiredState.Active;
        activeCell.ActualState = WorldCellActualState.Active;

        loadedCell.DesiredState = WorldCellDesiredState.Active;
        loadedCell.ActualState = WorldCellActualState.Loaded;

        var priorityCtx = new WorldCellPriorityContext(
            CameraBlockPos: default,
            AnchorBlockPos: default,
            HasAnchor: false,
            WindowMinRegion: default,
            WindowMaxRegion: default,
            HasWindow: false,
            NowTick: 0,
            IsCellLikelyLoaded: null);

        activeCell.EnqueueStateAndWork(scheduler, in priorityCtx);
        loadedCell.EnqueueStateAndWork(scheduler, in priorityCtx);

        Assert.Equal(1, scheduler.CaptureCount);
        Assert.Equal(1, scheduler.RelightCount);

        Span<WorldCellKey> capture = stackalloc WorldCellKey[4];
        Span<WorldCellKey> relight = stackalloc WorldCellKey[4];

        int capN = scheduler.CopyTopCaptureKeys(capture);
        int relN = scheduler.CopyTopRelightKeys(relight);

        Assert.True(capN > 0);
        Assert.True(relN > 0);

        Assert.Equal(activeCell.Key, capture[0]);
        Assert.Equal(activeCell.Key, relight[0]);
     }
}
