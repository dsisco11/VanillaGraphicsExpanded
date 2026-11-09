using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks LumOn scene physical pool planner behavior and contract rules.</summary>
public sealed class LumonScenePhysicalPoolPlannerTests
{
    #region Public API
    /// <summary>Verifies the create near plan uses chunk budget pages and computes atlas count behavior.</summary>
    [Fact]
    public void CreateNearPlan_UsesChunkBudgetPagesAndComputesAtlasCount()
    {
        // Arrange the near-field resolution, chunk radii, per-chunk budget and atlas cap.
        // Act by planning the near pool, then assert tile geometry, page capacity and atlas count.
        LumonScenePhysicalPoolPlan p = LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: 4,
            nearRadiusXZChunks: 8,
            nearRadiusYChunks: 0,
            nearPagesPerChunkBudget: 16,
            maxAtlasCount: 64);
        // Assert
        Assert.Equal(LumonSceneField.Near, p.Field);
        Assert.Equal(16, p.TileSizeTexels);
        Assert.Equal(64, p.TilesPerAxis);
        Assert.Equal(64 * 64, p.TilesPerAtlas);
        Assert.Equal(323 * 16, p.RequestedPages);
        Assert.Equal(323 * 16, p.CapacityPages);
        Assert.Equal(2, p.AtlasCount);
        Assert.False(p.IsClampedByMaxAtlases);
    }

    /// <summary>Verifies the create far plan annulus uses annulus chunk budget pages behavior.</summary>
    [Fact]
    public void CreateFarPlanAnnulus_UsesAnnulusChunkBudgetPages()
    {
        // Arrange a far-field annulus around the near box with one page per chunk.
        // Act by planning the far pool, then assert annulus pages and atlas capacity.
        LumonScenePhysicalPoolPlan p = LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            farTexelsPerVoxelFaceEdge: 1,
            nearRadiusXZChunks: 8,
            nearRadiusYChunks: 0,
            farRadiusXZChunks: 32,
            farRadiusYChunks: 0,
            farPagesPerChunkBudget: 1,
            maxAtlasCount: 64);
        // Assert
        Assert.Equal(LumonSceneField.Far, p.Field);
        Assert.Equal(4, p.TileSizeTexels);
        Assert.Equal(256, p.TilesPerAxis);
        Assert.Equal(256 * 256, p.TilesPerAtlas);
        Assert.Equal((4225 - 289) + 130, p.RequestedPages);
        Assert.Equal(p.RequestedPages, p.CapacityPages);
        Assert.Equal(1, p.AtlasCount);
        Assert.False(p.IsClampedByMaxAtlases);
    }
    #endregion
}











