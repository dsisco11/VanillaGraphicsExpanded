using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene physical page pool behavior and contract rules.</summary>
public sealed class LumonScenePhysicalPagePoolTests
{
    #region Public API
    /// <summary>Verifies the allocate and free reuses pages behavior.</summary>
    [Fact]
    public void AllocateAndFree_ReusesPages()
    {
        // Arrange
        LumonScenePhysicalPoolPlan plan = LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 4,
            nearRadiusXZChunks: 0,
            nearRadiusYChunks: 0,
            nearPagesPerChunkBudget: 1,
            maxAtlasCount: 64);

        var pool = new LumonScenePhysicalPagePool(plan);
        Assert.Equal(3, pool.CapacityPages);

        // Act and Assert
        Assert.True(pool.TryAllocate(out LumonScenePhysicalPage p0));
        Assert.True(pool.TryAllocate(out LumonScenePhysicalPage p1));
        Assert.True(pool.TryAllocate(out LumonScenePhysicalPage p2));
        Assert.False(pool.TryAllocate(out _));
        pool.Free(p1.PhysicalPageId);
        Assert.True(pool.TryAllocate(out LumonScenePhysicalPage p1b));
        Assert.Equal(p1.PhysicalPageId, p1b.PhysicalPageId);
    }
    #endregion
}











