using System;

using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal sealed class LumonScenePhysicalPoolManager : IDisposable
{
    public const int MaxAtlasCountDefault = 64;

    private readonly LumonScenePhysicalFieldPool near = new(LumonSceneField.Near);
    private readonly LumonScenePhysicalFieldPool far = new(LumonSceneField.Far);

    public LumonScenePhysicalFieldPool Near => near;
    public LumonScenePhysicalFieldPool Far => far;

    /// <summary>Plans both fields under the shared live byte budget before admitting GPU allocations.</summary>
    public void ConfigureFrom(in VgeConfig.LumOnSettingsConfig.LumonSceneConfig cfg, int maxAtlasCount = MaxAtlasCountDefault)
    {
        // Split the total layer budget between the two fields before allocating either.
        int byteLimitedAtlases = (int)(LumonScenePhysicalAtlasGpuResources.TotalByteBudget /
            (2L * LumonScenePhysicalAtlasGpuResources.BytesPerTexel *
             LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels * LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels));
        maxAtlasCount = Math.Min(maxAtlasCount, byteLimitedAtlases);
        LumonScenePhysicalPoolPlan nearPlan = LumonScenePhysicalPoolPlanner.CreateNearPlan(
            cfg.NearTexelsPerVoxelFaceEdge,
            cfg.NearRadiusChunks,
            cfg.NearRadiusYChunks,
            cfg.NearPagesPerChunkBudget,
            maxAtlasCount);

        LumonScenePhysicalPoolPlan farPlan = LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            cfg.FarTexelsPerVoxelFaceEdge,
            cfg.NearRadiusChunks,
            cfg.NearRadiusYChunks,
            cfg.FarRadiusChunks,
            cfg.FarRadiusYChunks,
            cfg.FarPagesPerChunkBudget,
            maxAtlasCount);

        near.Configure(nearPlan);
        far.Configure(farPlan);
    }

    /// <summary>
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsureGpuResources()
    {
        near.EnsureGpuResources();
        far.EnsureGpuResources();
    }

    public void Dispose()
    {
        near.Dispose();
        far.Dispose();
    }
}
