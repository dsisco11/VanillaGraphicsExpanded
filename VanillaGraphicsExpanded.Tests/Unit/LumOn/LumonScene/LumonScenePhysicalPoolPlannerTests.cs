using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonScenePhysicalPoolPlannerTests
{
    [Fact]
    public void CreateNearPlan_UsesChunkBudgetPagesAndComputesAtlasCount()
    {
        LumonScenePhysicalPoolPlan p = LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 4,
            nearRadiusXZChunks: 8,
            nearRadiusYChunks: 0,
            maxAtlasCount: 64);

        Assert.Equal(LumonSceneField.Near, p.Field);
        Assert.Equal(16, p.TileSizeTexels);
        Assert.Equal(256, p.TilesPerAxis);
        Assert.Equal(256 * 256, p.TilesPerAtlas);
        Assert.Equal(323, p.RequestedPages);
        Assert.Equal(323, p.CapacityPages);
        Assert.Equal(1, p.AtlasCount);
        Assert.False(p.IsClampedByMaxAtlases);
    }

    [Fact]
    public void CreateFarPlanAnnulus_UsesAnnulusChunkBudgetPages()
    {
        LumonScenePhysicalPoolPlan p = LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            farTexelsPerVoxelFaceEdge: 1,
            nearRadiusXZChunks: 8,
            nearRadiusYChunks: 0,
            farRadiusXZChunks: 32,
            farRadiusYChunks: 0,
            maxAtlasCount: 64);

        Assert.Equal(LumonSceneField.Far, p.Field);
        Assert.Equal(4, p.TileSizeTexels);
        Assert.Equal(1024, p.TilesPerAxis);
        Assert.Equal(1024 * 1024, p.TilesPerAtlas);
        Assert.Equal((4225 - 289) + 130, p.RequestedPages);
        Assert.Equal(p.RequestedPages, p.CapacityPages);
        Assert.Equal(1, p.AtlasCount);
        Assert.False(p.IsClampedByMaxAtlases);
    }
}

