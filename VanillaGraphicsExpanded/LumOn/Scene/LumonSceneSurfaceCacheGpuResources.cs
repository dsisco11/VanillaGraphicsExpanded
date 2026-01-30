using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Bundles Phase 22.5 GPU resources (page tables, patch metadata, work queues) for Near/Far fields.
/// </summary>
internal sealed class LumonSceneSurfaceCacheGpuResources : IDisposable
{
    private readonly LumonSceneFieldGpuResources near = new(LumonSceneField.Near);
    private readonly LumonSceneFieldGpuResources far = new(LumonSceneField.Far);

    public LumonSceneFieldGpuResources Near => near;
    public LumonSceneFieldGpuResources Far => far;

    public void ConfigureFrom(LumonScenePhysicalPoolManager pools)
    {
        if (pools is null) throw new ArgumentNullException(nameof(pools));

        // v1: ChunkSlotCount == RequestedPages (1 guaranteed page per chunk in the window).
        // If later we decouple these, chunk slots should be configured separately.
        near.Configure(
            chunkSlotCount: pools.Near.Plan.RequestedPages <= 0 ? 1 : pools.Near.Plan.RequestedPages,
            physicalPageCapacity: pools.Near.Plan.CapacityPages <= 0 ? 1 : pools.Near.Plan.CapacityPages);

        far.Configure(
            chunkSlotCount: pools.Far.Plan.RequestedPages <= 0 ? 1 : pools.Far.Plan.RequestedPages,
            physicalPageCapacity: pools.Far.Plan.CapacityPages <= 0 ? 1 : pools.Far.Plan.CapacityPages);
    }

    /// <summary>
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsureCreated()
    {
        near.EnsureCreated();
        far.EnsureCreated();
    }

    public void Dispose()
    {
        near.Dispose();
        far.Dispose();
    }
}

internal sealed class LumonSceneFieldGpuResources : IDisposable
{
    private readonly LumonSceneField field;

    private LumonScenePageTableGpuResources? pageTable;
    private LumonScenePatchMetadataGpuBuffer? patchMetadata;

    private LumonSceneWorkQueueGpu<LumonScenePageRequestGpu>? pageRequests;
    private LumonSceneWorkQueueGpu<LumonSceneCaptureWorkGpu>? captureWork;
    private LumonSceneWorkQueueGpu<LumonSceneRelightWorkGpu>? relightWork;

    public LumonSceneField Field => field;

    public LumonScenePageTableGpuResources PageTable => pageTable ?? throw new InvalidOperationException("Not configured.");
    public LumonScenePatchMetadataGpuBuffer PatchMetadata => patchMetadata ?? throw new InvalidOperationException("Not configured.");

    public LumonSceneWorkQueueGpu<LumonScenePageRequestGpu> PageRequests => pageRequests ?? throw new InvalidOperationException("Not configured.");
    public LumonSceneWorkQueueGpu<LumonSceneCaptureWorkGpu> CaptureWork => captureWork ?? throw new InvalidOperationException("Not configured.");
    public LumonSceneWorkQueueGpu<LumonSceneRelightWorkGpu> RelightWork => relightWork ?? throw new InvalidOperationException("Not configured.");

    public LumonSceneFieldGpuResources(LumonSceneField field)
    {
        this.field = field;
    }

    public void Configure(int chunkSlotCount, int physicalPageCapacity)
    {
        if (chunkSlotCount <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSlotCount));
        if (physicalPageCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(physicalPageCapacity));

        pageTable?.Dispose();
        patchMetadata?.Dispose();
        pageRequests?.Dispose();
        captureWork?.Dispose();
        relightWork?.Dispose();

        pageTable = new LumonScenePageTableGpuResources(field, chunkSlotCount);

        // v1: metadata indexed by physicalPageId, so allocate +1 for id=0 invalid.
        patchMetadata = new LumonScenePatchMetadataGpuBuffer(field, capacityEntries: physicalPageCapacity + 1);

        // Queue capacities (v1 defaults): tuned later by profiling/budgets.
        int q = Math.Max(1024, physicalPageCapacity);
        pageRequests = new LumonSceneWorkQueueGpu<LumonScenePageRequestGpu>(
            $"LumOn.LumonScene.{field}.PageRequests",
            capacityItems: q,
            counterUsage: OpenTK.Graphics.OpenGL.BufferUsageHint.DynamicRead,
            itemsUsage: OpenTK.Graphics.OpenGL.BufferUsageHint.DynamicRead);
        captureWork = new LumonSceneWorkQueueGpu<LumonSceneCaptureWorkGpu>($"LumOn.LumonScene.{field}.CaptureWork", capacityItems: q);
        relightWork = new LumonSceneWorkQueueGpu<LumonSceneRelightWorkGpu>($"LumOn.LumonScene.{field}.RelightWork", capacityItems: q);
    }

    /// <summary>
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsureCreated()
    {
        if (pageTable is null || patchMetadata is null || pageRequests is null || captureWork is null || relightWork is null)
        {
            throw new InvalidOperationException("Field resources must be configured before creation.");
        }

        pageTable.EnsureCreated();
        patchMetadata.EnsureCreated();

        pageRequests.EnsureCreated();
        captureWork.EnsureCreated();
        relightWork.EnsureCreated();
    }

    public void Dispose()
    {
        pageTable?.Dispose();
        patchMetadata?.Dispose();
        pageRequests?.Dispose();
        captureWork?.Dispose();
        relightWork?.Dispose();

        pageTable = null;
        patchMetadata = null;
        pageRequests = null;
        captureWork = null;
        relightWork = null;
    }
}

