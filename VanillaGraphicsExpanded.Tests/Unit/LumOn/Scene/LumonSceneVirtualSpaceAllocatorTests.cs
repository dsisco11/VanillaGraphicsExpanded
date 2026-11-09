using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene virtual space allocator behavior and contract rules.</summary>
public sealed class LumonSceneVirtualSpaceAllocatorTests
{
    #region Public API
    /// <summary>Verifies the try allocate one page maps patch id to virtual coord behavior.</summary>
    [Fact]
    public void TryAllocateOnePage_MapsPatchIdToVirtualCoord()
    {
        // Arrange
        var alloc = new LumonSceneVirtualSpaceAllocator();
        // Act and Assert
        Assert.True(alloc.TryAllocateOnePage(new LumonScenePatchId(0), out ushort x0, out ushort y0));
        Assert.Equal(0, x0);
        Assert.Equal(0, y0);

        Assert.True(alloc.TryAllocateOnePage(new LumonScenePatchId(127), out ushort x127, out ushort y127));
        Assert.Equal(127, x127);
        Assert.Equal(0, y127);

        Assert.True(alloc.TryAllocateOnePage(new LumonScenePatchId(128), out ushort x128, out ushort y128));
        Assert.Equal(0, x128);
        Assert.Equal(1, y128);
    }

    /// <summary>Verifies the try allocate one page rejects out of range behavior.</summary>
    [Fact]
    public void TryAllocateOnePage_RejectsOutOfRange()
    {
        // Arrange
        var alloc = new LumonSceneVirtualSpaceAllocator();
        int max = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
        // Act and Assert
        Assert.False(alloc.TryAllocateOnePage(new LumonScenePatchId(max), out _, out _));
    }
    #endregion
}












