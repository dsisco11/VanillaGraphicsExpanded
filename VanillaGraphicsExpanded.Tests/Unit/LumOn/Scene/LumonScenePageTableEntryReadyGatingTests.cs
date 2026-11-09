using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene page table entry ready gating behavior and contract rules.</summary>
public sealed class LumonScenePageTableEntryReadyGatingTests
{
    #region Public API
    /// <summary>Verifies the is ready for sampling false when physical id is zero behavior.</summary>
    [Fact]
    public void IsReadyForSampling_False_WhenPhysicalIdIsZero()
    {
        // Arrange
        var entry = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 0,
            flags: LumonScenePageTableEntryPacking.Flags.Resident);
        // Act and Assert
        Assert.False(LumonScenePageTableEntryPacking.IsReadyForSampling(entry));
    }
    /// <summary>Verifies the is ready for sampling false when not resident behavior.</summary>
    [Fact]
    public void IsReadyForSampling_False_WhenNotResident()
    {
        // Arrange
        var entry = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1,
            flags: LumonScenePageTableEntryPacking.Flags.None);
        // Act and Assert
        Assert.False(LumonScenePageTableEntryPacking.IsReadyForSampling(entry));
    }
    /// <summary>Verifies the is ready for sampling false when needs capture or relight behavior.</summary>
    [Fact]
    public void IsReadyForSampling_False_WhenNeedsCaptureOrRelight()
    {
        // Arrange
        var entryNeedsCapture = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsCapture);
        // Act and Assert
        Assert.False(LumonScenePageTableEntryPacking.IsReadyForSampling(entryNeedsCapture));

        var entryNeedsRelight = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 1,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsRelight);
        Assert.False(LumonScenePageTableEntryPacking.IsReadyForSampling(entryNeedsRelight));
    }

    /// <summary>Verifies the is ready for sampling true when resident and no pending work behavior.</summary>
    [Fact]
    public void IsReadyForSampling_True_WhenResidentAndNoPendingWork()
    {
        // Arrange
        var entry = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 123,
            flags: LumonScenePageTableEntryPacking.Flags.Resident);
        // Act and Assert
        Assert.True(LumonScenePageTableEntryPacking.IsReadyForSampling(entry));
    }
    #endregion
}












