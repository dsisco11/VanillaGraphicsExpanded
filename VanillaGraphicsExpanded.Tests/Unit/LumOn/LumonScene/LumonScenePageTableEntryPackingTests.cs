using System.Runtime.InteropServices;

using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonScenePageTableEntryPackingTests
{
    [Fact]
    public void PackUnpack_RoundTrips()
    {
        var e = LumonScenePageTableEntryPacking.Pack(
            physicalPageId: 12345,
            flags: LumonScenePageTableEntryPacking.Flags.Resident | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

        Assert.Equal((uint)12345, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(e));
        var flags = LumonScenePageTableEntryPacking.UnpackFlags(e);
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.Resident));
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.NeedsRelight));
        Assert.False(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.NeedsCapture));
    }

    [Fact]
    public void PageTableEntry_IsFourBytes()
    {
        Assert.Equal(4, Marshal.SizeOf<LumonScenePageTableEntry>());
    }
}

