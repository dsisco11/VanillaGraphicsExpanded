using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal sealed class LumonScenePhysicalFieldPool : IDisposable
{
    private readonly LumonSceneField field;

    private LumonScenePhysicalPoolPlan plan;
    private LumonScenePhysicalPagePool? pagePool;
    private LumonScenePhysicalAtlasGpuResources? gpuResources;

    public LumonSceneField Field => field;
    public LumonScenePhysicalPoolPlan Plan => plan;
    public LumonScenePhysicalPagePool PagePool => pagePool ?? throw new InvalidOperationException("Pool not configured.");
    public LumonScenePhysicalAtlasGpuResources? GpuResources => gpuResources;

    public long AllocCount { get; private set; }
    public long FreeCount { get; private set; }
    public long EvictionCandidateCount { get; private set; }

    public LumonScenePhysicalFieldPool(LumonSceneField field)
    {
        this.field = field;
        plan = default;
    }

    public void Configure(in LumonScenePhysicalPoolPlan newPlan)
    {
        if (newPlan.Field != field)
        {
            throw new ArgumentOutOfRangeException(nameof(newPlan), "Field mismatch.");
        }

        // ConfigureFrom() is called each frame; avoid resetting residency state when the plan is unchanged.
        // Resetting the page pool would invalidate the CPU virtual->physical mappings and cause atlas thrash/black pages.
        if (pagePool is not null
            && plan.TileSizeTexels == newPlan.TileSizeTexels
            && plan.TilesPerAxis == newPlan.TilesPerAxis
            && plan.TilesPerAtlas == newPlan.TilesPerAtlas
            && plan.RequestedPages == newPlan.RequestedPages
            && plan.CapacityPages == newPlan.CapacityPages
            && plan.AtlasCount == newPlan.AtlasCount
            && plan.IsClampedByMaxAtlases == newPlan.IsClampedByMaxAtlases)
        {
            return;
        }

        bool needsGpuRecreate =
            gpuResources is null ||
            gpuResources.AtlasCount != newPlan.AtlasCount ||
            gpuResources.TileSizeTexels != newPlan.TileSizeTexels;

        plan = newPlan;
        pagePool = new LumonScenePhysicalPagePool(newPlan);

        AllocCount = 0;
        FreeCount = 0;
        EvictionCandidateCount = 0;

        if (needsGpuRecreate)
        {
            gpuResources?.Dispose();
            gpuResources = null;
        }
    }

    /// <summary>
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsureGpuResources()
    {
        if (pagePool is null)
        {
            throw new InvalidOperationException("Pool not configured.");
        }

        if (gpuResources is not null)
        {
            return;
        }

        gpuResources = new LumonScenePhysicalAtlasGpuResources(field, plan.AtlasCount, plan.TileSizeTexels);
    }

    public bool TryAllocate(out LumonScenePhysicalPage page)
    {
        if (PagePool.TryAllocate(out page))
        {
            AllocCount++;
            return true;
        }

        return false;
    }

    public bool TryGetEvictionCandidate(out uint physicalPageId)
    {
        if (PagePool.TryGetEvictionCandidate(out physicalPageId))
        {
            EvictionCandidateCount++;
            return true;
        }

        return false;
    }

    public void Free(uint physicalPageId)
    {
        PagePool.Free(physicalPageId);
        FreeCount++;
    }

    public void Dispose()
    {
        gpuResources?.Dispose();
        gpuResources = null;
        pagePool = null;
    }
}

