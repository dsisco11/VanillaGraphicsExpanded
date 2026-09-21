using System.Collections.Generic;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.WorldPartition;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneRegionCellTransitionLegalityTests
{
    private sealed class RecordingSink : IWorldCellWorkSink
    {
        public long NowTick { get; set; }

        public readonly Dictionary<(WorldCellKey Key, WorldCellWorkQueue Queue), float> Upserts = new();

        public void Upsert(WorldCellKey key, WorldCellWorkQueue queue, float priority)
            => Upserts[(key, queue)] = priority;

        public void Remove(WorldCellKey key, WorldCellWorkQueue queue)
            => Upserts.Remove((key, queue));

        public void SetCooldown(WorldCellKey key, long tick)
        {
            _ = key;
            _ = tick;
        }
    }

    [Fact]
    public void LoadedOnlyCells_DoNotEnqueueCaptureOrRelightWork()
    {
        var cell = new LumonSceneRegionCell(
            kind: WorldCellKind.LumonSceneNear,
            chunkCoord: new LumonSceneChunkCoord(0, 0, 0));

        // Assign a slot and synthesize backlog.
        cell.UpdateSlotAssignment(chunkSlot: 0, slotGeneration: 1, nowTick: 0);

        var mirror = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        mirror[0] = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsCapture);

        mirror[1] = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 2,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

        _ = cell.TryUpdateBacklogFromPageTableMirror(mirror);

        // Desired Active, but not yet actually Active.
        cell.DesiredState = WorldCellDesiredState.Active;
        cell.ActualState = WorldCellActualState.Loaded;

        Assert.True(cell.NeedsCapturePages > 0);
        Assert.True(cell.NeedsRelightPages > 0);

        var sink = new RecordingSink();
        var ctx = new WorldCellPriorityContext(
            CameraBlockPos: default,
            AnchorBlockPos: default,
            HasAnchor: false,
            WindowMinRegion: default,
            WindowMaxRegion: default,
            HasWindow: false,
            NowTick: 0,
            IsCellLikelyLoaded: null);

        cell.EnqueueStateAndWork(sink, in ctx);

        Assert.DoesNotContain(sink.Upserts.Keys, k => k.Queue == WorldCellWorkQueue.Capture);
        Assert.DoesNotContain(sink.Upserts.Keys, k => k.Queue == WorldCellWorkQueue.Relight);
        // Domain cells no longer enqueue residency transitions; the coordinator is their only owner.
        Assert.Empty(sink.Upserts);
    }
}
