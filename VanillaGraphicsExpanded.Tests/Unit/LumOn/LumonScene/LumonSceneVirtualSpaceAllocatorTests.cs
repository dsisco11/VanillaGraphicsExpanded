using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneVirtualSpaceAllocatorTests
{
    [Fact]
    public void TryAllocateOnePage_MapsPatchIdToVirtualCoord()
    {
        var alloc = new LumonSceneVirtualSpaceAllocator();

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

    [Fact]
    public void TryAllocateOnePage_RejectsOutOfRange()
    {
        var alloc = new LumonSceneVirtualSpaceAllocator();
        int max = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
        Assert.False(alloc.TryAllocateOnePage(new LumonScenePatchId(max), out _, out _));
    }
}

