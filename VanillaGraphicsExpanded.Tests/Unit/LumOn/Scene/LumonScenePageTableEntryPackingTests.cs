using System.Runtime.InteropServices;

using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene page table entry packing behavior and contract rules.</summary>
public sealed class LumonScenePageTableEntryPackingTests
{
    #region Public API
    /// <summary>Verifies the pack unpack round trips behavior.</summary>
    [Fact]
    public void PackUnpack_RoundTrips()
    {
        // Arrange a resident page that also needs relighting, but does not need capture.
        // Act by packing the page and flags, then assert each decoded field and flag.
        var e = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 12345,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsRelight);
        // Assert
        Assert.Equal((uint)12345, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(e));
        var flags = LumonScenePageTableEntryPacking.UnpackFlags(e);
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.Resident));
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.NeedsRelight));
        Assert.False(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.NeedsCapture));
    }

    /// <summary>Verifies the page table entry is four bytes behavior.</summary>
    [Fact]
    public void PageTableEntry_IsFourBytes()
    {
        // Arrange no value: verify the page-table entry's managed storage contract directly.
        // Act and assert the entry occupies one 32-bit word.
        Assert.Equal(4, Marshal.SizeOf<LumonScenePageTableEntry>());
    }
    #endregion
}












