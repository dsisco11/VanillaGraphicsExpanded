using System;

using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.LumOn.LumonScene;

internal sealed class LumonScenePhysicalPoolManager : IDisposable
{
    public const int MaxAtlasCountDefault = 64;

    private readonly LumonScenePhysicalFieldPool near = new(LumonSceneField.Near);
    private readonly LumonScenePhysicalFieldPool far = new(LumonSceneField.Far);

    public LumonScenePhysicalFieldPool Near => near;
    public LumonScenePhysicalFieldPool Far => far;

    public void ConfigureFrom(in VgeConfig.LumOnSettingsConfig.LumonSceneConfig cfg, int maxAtlasCount = MaxAtlasCountDefault)
    {
        LumonScenePhysicalPoolPlan nearPlan = LumonScenePhysicalPoolPlanner.CreateNearPlan(
            cfg.NearTexelsPerVoxelFaceEdge,
            cfg.NearRadiusChunks,
            maxAtlasCount);

        LumonScenePhysicalPoolPlan farPlan = LumonScenePhysicalPoolPlanner.CreateFarPlanAnnulus(
            cfg.FarTexelsPerVoxelFaceEdge,
            cfg.NearRadiusChunks,
            cfg.FarRadiusChunks,
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

