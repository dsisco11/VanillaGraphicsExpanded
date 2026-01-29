using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneVirtualPagedAtlasTests
{
    [Fact]
    public void VoxelPatchLayout_IsCenteredWithinTile()
    {
        Assert.Equal(16, LumonSceneVoxelPatchLayout.DefaultNear.TileSizeTexels);
        Assert.Equal(16, LumonSceneVoxelPatchLayout.DefaultNear.PatchSizeTexels);
        Assert.Equal(0, LumonSceneVoxelPatchLayout.DefaultNear.InTileOffsetTexels);
        Assert.Equal(0, LumonSceneVoxelPatchLayout.DefaultNear.BorderTexels);
    }

    [Fact]
    public void TryGetVoxelPatchHandle_IsDeterministic()
    {
        var atlas = new LumonSceneVirtualPagedAtlas();

        Assert.True(atlas.TryGetVoxelPatchHandle(new LumonScenePatchId(0), out LumonSceneVirtualHandle h0));
        Assert.True(atlas.TryGetVoxelPatchHandle(new LumonScenePatchId(0), out LumonSceneVirtualHandle h0b));
        Assert.Equal(h0.VirtualPageX, h0b.VirtualPageX);
        Assert.Equal(h0.VirtualPageY, h0b.VirtualPageY);
        Assert.Equal((ushort)0, h0.InTileOffsetX);
        Assert.Equal((ushort)0, h0.InTileOffsetY);

        Assert.True(atlas.TryGetVoxelPatchHandle(new LumonScenePatchId(1), out LumonSceneVirtualHandle h1));
        Assert.NotEqual(h0.VirtualPageX, h1.VirtualPageX);
        Assert.Equal(h0.VirtualPageY, h1.VirtualPageY);
    }

    [Fact]
    public void TryGetVoxelPatchHandle_AllowsAlternateLayouts()
    {
        var atlas = new LumonSceneVirtualPagedAtlas();

        var farLayout = LumonSceneVoxelPatchLayout.Create(texelsPerVoxelFaceEdge: 8);
        Assert.True(atlas.TryGetVoxelPatchHandle(new LumonScenePatchId(42), farLayout, out LumonSceneVirtualHandle h));
        Assert.Equal((ushort)farLayout.InTileOffsetTexels, h.InTileOffsetX);
        Assert.Equal((ushort)farLayout.PatchSizeTexels, h.PatchSizeTexelsX);
    }

    [Fact]
    public void TryGetVoxelPatchHandle_RejectsOutOfRangePatchIds()
    {
        var atlas = new LumonSceneVirtualPagedAtlas();
        int max = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
        Assert.False(atlas.TryGetVoxelPatchHandle(new LumonScenePatchId(max), out _));
    }
}
