using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonScenePageTableEntryReadyGatingTests
{
    [Fact]
    public void IsReadyForSampling_False_WhenPhysicalIdIsZero()
    {
        var entry = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 0,
            flags: LumonScenePageTableEntryPacking.Flags.Resident);

        Assert.False(LumonScenePageTableEntryPacking.IsReadyForSampling(entry));
    }

    [Fact]
    public void IsReadyForSampling_False_WhenNotResident()
    {
        var entry = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1,
            flags: LumonScenePageTableEntryPacking.Flags.None);

        Assert.False(LumonScenePageTableEntryPacking.IsReadyForSampling(entry));
    }

    [Fact]
    public void IsReadyForSampling_False_WhenNeedsCaptureOrRelight()
    {
        var entryNeedsCapture = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsCapture);
        Assert.False(LumonScenePageTableEntryPacking.IsReadyForSampling(entryNeedsCapture));

        var entryNeedsRelight = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsRelight);
        Assert.False(LumonScenePageTableEntryPacking.IsReadyForSampling(entryNeedsRelight));
    }

    [Fact]
    public void IsReadyForSampling_True_WhenResidentAndNoPendingWork()
    {
        var entry = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 123,
            flags: LumonScenePageTableEntryPacking.Flags.Resident);

        Assert.True(LumonScenePageTableEntryPacking.IsReadyForSampling(entry));
    }
}

