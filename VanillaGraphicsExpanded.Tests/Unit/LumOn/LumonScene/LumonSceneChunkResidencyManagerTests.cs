using System.Collections.Generic;

using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneChunkResidencyManagerTests
{
    [Fact]
    public void ChunkUnload_FreesPage()
    {
        var pools = new LumonScenePhysicalPoolManager();
        pools.Near.Configure(LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 4,
            nearRadiusChunks: 0,
            maxAtlasCount: 64));
        pools.Far.Configure(LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            farTexelsPerVoxelFaceEdge: 1,
            nearRadiusChunks: 0,
            farRadiusChunks: 0,
            maxAtlasCount: 64));

        var residency = new LumonSceneChunkResidencyManager(pools);

        var chunk = new LumonSceneChunkCoord(1, 2, 3);
        Assert.True(residency.TryActivateChunk(LumonSceneField.Near, chunk, out var page));
        Assert.Equal(1, residency.ActiveChunksNear);

        int freeBefore = residency.FreePagesNear;
        residency.OnChunkUnloaded(chunk);
        Assert.Equal(0, residency.ActiveChunksNear);
        Assert.True(residency.FreePagesNear > freeBefore);

        // After unload, the chunk should be gone.
        Assert.False(residency.TryGetChunkPage(chunk, out _, out _));
        Assert.True(page.PhysicalPageId != 0);
    }

    [Fact]
    public void Eviction_UnderPressure_ReleasesOldest()
    {
        var pools = new LumonScenePhysicalPoolManager();

        // Force a tiny capacity per atlas: tileSize=2048 -> 2x2 tiles => 4 pages.
        // Then clamp to 1 atlas so requested pages > capacity => eviction path engaged.
        pools.Near.Configure(LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 512,
            nearRadiusChunks: 1,
            maxAtlasCount: 1));
        pools.Far.Configure(LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            farTexelsPerVoxelFaceEdge: 512,
            nearRadiusChunks: 0,
            farRadiusChunks: 0,
            maxAtlasCount: 1));

        var residency = new LumonSceneChunkResidencyManager(pools);

        var released = new List<LumonScenePageReleasedEvent>();
        residency.PageReleased += released.Add;

        // Activate more chunks than capacity; expect evictions.
        for (int i = 0; i < 8; i++)
        {
            var chunk = new LumonSceneChunkCoord(i, 0, 0);
            Assert.True(residency.TryActivateChunk(LumonSceneField.Near, chunk, out _));
        }

        Assert.Equal(4, residency.ActiveChunksNear);
        Assert.True(residency.EvictionsNear > 0);
        Assert.Contains(released, e => e.Field == LumonSceneField.Near && e.Reason == LumonScenePageReleaseReason.Evicted);
    }

    [Fact]
    public void FieldTransition_ReleasesOldFieldPage()
    {
        var pools = new LumonScenePhysicalPoolManager();
        pools.Near.Configure(LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 4,
            nearRadiusChunks: 0,
            maxAtlasCount: 64));
        pools.Far.Configure(LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            farTexelsPerVoxelFaceEdge: 1,
            nearRadiusChunks: 0,
            farRadiusChunks: 0,
            maxAtlasCount: 64));

        var residency = new LumonSceneChunkResidencyManager(pools);

        var released = new List<LumonScenePageReleasedEvent>();
        residency.PageReleased += released.Add;

        var chunk = new LumonSceneChunkCoord(7, 0, 9);
        Assert.True(residency.TryActivateChunk(LumonSceneField.Far, chunk, out var farPage));
        Assert.True(residency.TryActivateChunk(LumonSceneField.Near, chunk, out var nearPage));

        Assert.NotEqual(0u, farPage.PhysicalPageId);
        Assert.NotEqual(0u, nearPage.PhysicalPageId);

        Assert.Contains(released, e =>
            e.Field == LumonSceneField.Far &&
            e.Chunk == chunk &&
            e.PhysicalPageId == farPage.PhysicalPageId &&
            e.Reason == LumonScenePageReleaseReason.FieldTransition);
    }
}

