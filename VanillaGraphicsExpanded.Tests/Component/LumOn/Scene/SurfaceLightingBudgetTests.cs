using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.Scene;

/// <summary>Guards total layer admission independently of OpenGL availability.</summary>
public sealed class SurfaceLightingBudgetTests
{
    #region Public API
    /// <summary>Large requested near and far pools cannot multiply the explicit total texture budget.</summary>
    [Fact]
    public void PlannerAccountsForEveryLightingAndMaterialLayer()
    {
        // Arrange
        using var pools = new LumonScenePhysicalPoolManager();
        var config = new VgeConfig.LumOnSettingsConfig.LumonSceneConfig
        {
            NearRadiusChunks=8,NearRadiusYChunks=2,FarRadiusChunks=32,FarRadiusYChunks=4,
            NearPagesPerChunkBudget=1024,FarPagesPerChunkBudget=1024
        };
        // Act
        pools.ConfigureFrom(config);
        long bytes=(long)(pools.Near.Plan.AtlasCount+pools.Far.Plan.AtlasCount) *
            LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels * LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels *
            LumonScenePhysicalAtlasGpuResources.BytesPerTexel;
        // Assert
        Assert.InRange(bytes,1,LumonScenePhysicalAtlasGpuResources.TotalByteBudget);
        Assert.True(pools.Near.Plan.IsClampedByMaxAtlases);
        Assert.True(pools.Far.Plan.IsClampedByMaxAtlases);
    }

    #endregion
}











