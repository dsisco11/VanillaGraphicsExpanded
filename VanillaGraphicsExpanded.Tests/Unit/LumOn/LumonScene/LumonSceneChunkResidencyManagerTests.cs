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
            nearRadiusXZChunks: 0,
            nearRadiusYChunks: 0,
            nearPagesPerChunkBudget: 1,
            maxAtlasCount: 64));
        pools.Far.Configure(LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            farTexelsPerVoxelFaceEdge: 1,
            nearRadiusXZChunks: 0,
            nearRadiusYChunks: 0,
            farRadiusXZChunks: 0,
            farRadiusYChunks: 0,
            farPagesPerChunkBudget: 1,
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
    public void Activation_WhenOverCapacity_FailsWithoutEviction()
    {
        var pools = new LumonScenePhysicalPoolManager();

        // Force a tiny capacity per atlas: tileSize=2048 -> 2x2 tiles => 4 pages.
        // Then clamp to 1 atlas so requested pages > capacity => eviction path engaged.
        pools.Near.Configure(LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 512,
            nearRadiusXZChunks: 1,
            nearRadiusYChunks: 0,
            nearPagesPerChunkBudget: 1,
            maxAtlasCount: 1));
        pools.Far.Configure(LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            farTexelsPerVoxelFaceEdge: 512,
            nearRadiusXZChunks: 0,
            nearRadiusYChunks: 0,
            farRadiusXZChunks: 0,
            farRadiusYChunks: 0,
            farPagesPerChunkBudget: 1,
            maxAtlasCount: 1));

        var residency = new LumonSceneChunkResidencyManager(pools);

        var released = new List<LumonScenePageReleasedEvent>();
        residency.PageReleased += released.Add;

        // Activate more chunks than capacity; activation must fail once the pool is exhausted.
        int ok = 0;
        int fail = 0;
        for (int i = 0; i < 8; i++)
        {
            var chunk = new LumonSceneChunkCoord(i, 0, 0);
            if (residency.TryActivateChunk(LumonSceneField.Near, chunk, out _))
            {
                ok++;
            }
            else
            {
                fail++;
            }
        }

        Assert.Equal(4, ok);
        Assert.Equal(4, fail);
        Assert.Equal(4, residency.ActiveChunksNear);
        Assert.Equal(0, residency.EvictionsNear);
        Assert.DoesNotContain(released, e => e.Field == LumonSceneField.Near && e.Reason == LumonScenePageReleaseReason.Evicted);
    }

    [Fact]
    public void FieldTransition_ReleasesOldFieldPage()
    {
        var pools = new LumonScenePhysicalPoolManager();
        pools.Near.Configure(LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 4,
            nearRadiusXZChunks: 0,
            nearRadiusYChunks: 0,
            nearPagesPerChunkBudget: 1,
            maxAtlasCount: 64));
        pools.Far.Configure(LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            farTexelsPerVoxelFaceEdge: 1,
            nearRadiusXZChunks: 0,
            nearRadiusYChunks: 0,
            farRadiusXZChunks: 0,
            farRadiusYChunks: 0,
            farPagesPerChunkBudget: 1,
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
