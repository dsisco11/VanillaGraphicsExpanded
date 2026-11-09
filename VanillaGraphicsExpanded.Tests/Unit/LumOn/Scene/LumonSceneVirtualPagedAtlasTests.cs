using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene virtual paged atlas behavior and contract rules.</summary>
public sealed class LumonSceneVirtualPagedAtlasTests
{
    #region Public API
    /// <summary>Verifies the voxel patch layout is centered within tile behavior.</summary>
    [Fact]
    public void VoxelPatchLayout_IsCenteredWithinTile()
    {
        // Arrange no custom layout: inspect the default near patch's tile contract.
        // Act and assert its tile, patch, offset and border dimensions.
        Assert.Equal(16, LumonSceneVoxelPatchLayout.DefaultNear.TileSizeTexels);
        Assert.Equal(16, LumonSceneVoxelPatchLayout.DefaultNear.PatchSizeTexels);
        Assert.Equal(0, LumonSceneVoxelPatchLayout.DefaultNear.InTileOffsetTexels);
        Assert.Equal(0, LumonSceneVoxelPatchLayout.DefaultNear.BorderTexels);
    }

    /// <summary>Verifies the try get voxel patch handle is deterministic behavior.</summary>
    [Fact]
    public void TryGetVoxelPatchHandle_IsDeterministic()
    {
        // Arrange
        var atlas = new LumonSceneVirtualPagedAtlas();
        // Act and Assert
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

    /// <summary>Verifies the try get voxel patch handle allows alternate layouts behavior.</summary>
    [Fact]
    public void TryGetVoxelPatchHandle_AllowsAlternateLayouts()
    {
        // Arrange
        var atlas = new LumonSceneVirtualPagedAtlas();
        var farLayout = LumonSceneVoxelPatchLayout.Create(texelsPerVoxelFaceEdge: 8);
        // Act and Assert
        Assert.True(atlas.TryGetVoxelPatchHandle(new LumonScenePatchId(42), farLayout, out LumonSceneVirtualHandle h));
        Assert.Equal((ushort)farLayout.InTileOffsetTexels, h.InTileOffsetX);
        Assert.Equal((ushort)farLayout.PatchSizeTexels, h.PatchSizeTexelsX);
    }

    /// <summary>Verifies the try get voxel patch handle rejects out of range patch ids behavior.</summary>
    [Fact]
    public void TryGetVoxelPatchHandle_RejectsOutOfRangePatchIds()
    {
        // Arrange
        var atlas = new LumonSceneVirtualPagedAtlas();
        int max = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
        // Act and Assert
        Assert.False(atlas.TryGetVoxelPatchHandle(new LumonScenePatchId(max), out _));
    }
    #endregion
}











