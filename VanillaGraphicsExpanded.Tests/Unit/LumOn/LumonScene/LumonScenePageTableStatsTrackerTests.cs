using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonScenePageTableStatsTrackerTests
{
    [Fact]
    public void ApplyEntryChange_UpdatesPerSlotAndTotals()
    {
        var tracker = new LumonScenePageTableStatsTracker();
        tracker.Reset(newChunkSlotCount: 2);

        Assert.Equal(0, tracker.Total.Resident);
        Assert.Equal(0, tracker.Total.ReadyToSample);

        LumonScenePageTableEntry empty = default;

        var alloc = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1u,
            flags: LumonScenePageTableEntryPacking.Flags.Resident
                | LumonScenePageTableEntryPacking.Flags.NeedsCapture
                | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

        tracker.ApplyEntryChange(chunkSlot: 1u, in empty, in alloc);

        Assert.Equal(1, tracker.Total.Resident);
        Assert.Equal(1, tracker.Total.NeedsCapture);
        Assert.Equal(1, tracker.Total.NeedsRelight);
        Assert.Equal(0, tracker.Total.ReadyToSample);

        LumonSceneChunkSlotPageTableStats s1 = tracker.GetSlot(1u);
        Assert.Equal(1, s1.Resident);
        Assert.Equal(1, s1.NeedsCapture);
        Assert.Equal(1, s1.NeedsRelight);
        Assert.Equal(0, s1.ReadyToSample);

        var ready = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1u,
            flags: LumonScenePageTableEntryPacking.Flags.Resident);

        tracker.ApplyEntryChange(chunkSlot: 1u, in alloc, in ready);

        Assert.Equal(1, tracker.Total.Resident);
        Assert.Equal(0, tracker.Total.NeedsCapture);
        Assert.Equal(0, tracker.Total.NeedsRelight);
        Assert.Equal(1, tracker.Total.ReadyToSample);

        tracker.ApplyEntryChange(chunkSlot: 1u, in ready, in empty);

        Assert.Equal(0, tracker.Total.Resident);
        Assert.Equal(0, tracker.Total.ReadyToSample);
        Assert.Equal(default, tracker.GetSlot(1u));
    }
}

