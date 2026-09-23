using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Guards total layer admission independently of OpenGL availability.</summary>
public sealed class SurfaceLightingBudgetTests
{
    #region Budget admission
    /// <summary>Large requested near and far pools cannot multiply the explicit total texture budget.</summary>
    [Fact]
    public void PlannerAccountsForEveryLightingAndMaterialLayer()
    {
        using var pools = new LumonScenePhysicalPoolManager();
        var config = new VgeConfig.LumOnSettingsConfig.LumonSceneConfig
        {
            NearRadiusChunks=8,NearRadiusYChunks=2,FarRadiusChunks=32,FarRadiusYChunks=4,
            NearPagesPerChunkBudget=1024,FarPagesPerChunkBudget=1024
        };
        pools.ConfigureFrom(config);
        long bytes=(long)(pools.Near.Plan.AtlasCount+pools.Far.Plan.AtlasCount) *
            LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels * LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels *
            LumonScenePhysicalAtlasGpuResources.BytesPerTexel;
        Assert.InRange(bytes,1,LumonScenePhysicalAtlasGpuResources.TotalByteBudget);
        Assert.True(pools.Near.Plan.IsClampedByMaxAtlases);
        Assert.True(pools.Far.Plan.IsClampedByMaxAtlases);
    }

    /// <summary>Over-budget allocations fail before invoking GL and return their reserved credit.</summary>
    [Fact]
    public void RejectedAllocationDoesNotConsumeCredit()
    {
        bool before=LumonScenePhysicalAtlasGpuResources.CanAllocate(1);
        Assert.Throws<InvalidOperationException>(() => new LumonScenePhysicalAtlasGpuResources(LumonSceneField.Near,7,4));
        Assert.Equal(before,LumonScenePhysicalAtlasGpuResources.CanAllocate(1));
    }
    #endregion
}
