using VanillaGraphicsExpanded.LumOn.LumonScene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonScenePhysicalPagePoolTests
{
    [Fact]
    public void AllocateAndFree_ReusesPages()
    {
        LumonScenePhysicalPoolPlan plan = LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 4,
            nearRadiusChunks: 0,
            maxAtlasCount: 64);

        var pool = new LumonScenePhysicalPagePool(plan);

        Assert.Equal(3, pool.CapacityPages);

        Assert.True(pool.TryAllocate(out LumonScenePhysicalPage p0));
        Assert.True(pool.TryAllocate(out LumonScenePhysicalPage p1));
        Assert.True(pool.TryAllocate(out LumonScenePhysicalPage p2));
        Assert.False(pool.TryAllocate(out _));

        pool.Free(p1.PhysicalPageId);
        Assert.True(pool.TryAllocate(out LumonScenePhysicalPage p1b));
        Assert.Equal(p1.PhysicalPageId, p1b.PhysicalPageId);
    }
}
