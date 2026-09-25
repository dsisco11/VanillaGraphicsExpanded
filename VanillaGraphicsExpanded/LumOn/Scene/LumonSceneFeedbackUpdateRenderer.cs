using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using System;
using System.Buffers;
using System.Linq;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 22.6: feedback-driven residency (v1).
/// Gathers page requests from the PatchIdGBuffer and allocates physical tiles over time.
/// </summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer : IRenderer, IDisposable
{
    private readonly System.Func<LumOnCameraState?> readCamera;
    private long diagnosticCaptureAttempts, diagnosticCaptureFailures, diagnosticCaptureReadFailures;
    private const double RenderOrderValue = 0.9998;
    private const int RenderRangeValue = 1;

    private const int VirtualPagesPerChunk = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
    private const int VirtualPageTableW = LumonSceneVirtualAtlasConstants.VirtualPageTableWidth;

    private const int DefaultMaxRequestsPerFrame = 1024;
    private const int DefaultMaxNewAllocationsPerFrame = 16;
    private const int FeedbackMarkDebugCounterCount = 3;

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly GBufferManager gBufferManager;
    private TraceGeometryRenderer? traceGeometry;

    private readonly LumonScenePhysicalPoolManager physicalPools = new();
    private readonly LumonSceneFieldGpuResources nearGpu = new(LumonSceneField.Near);

    private LumonSceneFeedbackMarkPagesComputeShader? feedbackMarkShader;
    private LumonSceneFeedbackCompactPagesComputeShader? feedbackCompactShader;
    private LumonSceneCaptureVoxelComputeShader? captureVoxelShader;

    private Texture3D? pageUsageStamp;
    private GpuAtomicCounterBuffer? feedbackMarkDebugCounters;
    private uint feedbackFrameStamp = 1u;


    private VectorInt3 slotOriginMinChunk;
    private VectorInt3 slotDims;
    private VectorInt3 slotRing;
    private VectorInt3[] slotOwners = Array.Empty<VectorInt3>();
    private System.Collections.Generic.Dictionary<VectorInt3, uint> chunkToSlot = new();
    private ushort[] slotGenerations = Array.Empty<ushort>();
    private Texture2D? slotGenerationTex;
    private LumonSceneChunkSlotInfoGpuBuffer? slotInfoBuffer;
    private VectorInt3 lastAnchorChunk;

    private LumonScenePageTableEntry[] pageTableMirror = Array.Empty<LumonScenePageTableEntry>();
    private System.Collections.Generic.Dictionary<ulong, uint> virtualToPhysical = new();
    private System.Collections.Generic.Dictionary<uint, ulong> physicalToVirtual = new();
    private readonly LumonScenePageTableStatsTracker pageTableStats = new();

    private LumonSceneFeedbackRequestProcessor? cpuProcessor;

    // Phase 9.1: chunk residency manager integration (eviction/unload safety).
    private LumonSceneChunkResidencyManager? chunkResidency;

    private readonly LumonSceneRegionScheduler nearRegionScheduler;
    private int lastRegionCells;
    private int lastRegionActive;
    private int lastRegionLoaded;
    private int lastRegionDesiredUnloaded;
    private int lastRegionDesiredLoaded;
    private int lastRegionDesiredActive;
    private int lastRegionSuppressed;
    private int lastLoadedWindowSize;
    private int lastActiveWindowSize;
    private int lastBudgetMaxRequests;
    private int lastBudgetMaxNewAllocs;
    private int lastBudgetMaxRelightPages;

    private WorldCellStateTransitionContext lastNearStateContext;
    private WorldCellPriorityContext lastNearPriorityContext;
    private bool hasLastNearContexts;

    private bool configured;
    private int lastPlanHash;


    private uint lastRequestCount;
    private int lastRequestsRead;
    private int lastRequestsProcessed;
    private int lastRequestsDroppedGpu;
    private int lastRequestsDroppedCpuBudget;
    private int lastRequestsDroppedInactive;
    private int lastRequestsPrioritized;
    private int lastPrioritizeTopK;
    private int lastCaptureCount;
    private int lastRelightCount;
    private LumonSceneFeedbackRequestProcessor.ProcessStats lastProcessStats;
    private int lastUniqueVirtualPages;
    private uint lastTopChunkSlot;
    private int lastTopVirtualPage;
    private int lastTopVirtualPageCount;
    private uint lastMarkRejectPatchId0;
    private uint lastMarkRejectChunkSlotOob;
    private uint lastMarkRejectGenMismatch;

    private uint[] lastTopChunkSlots = Array.Empty<uint>();
    private int[] lastTopChunkSlotCounts = Array.Empty<int>();
    private VectorInt3 lastWorldChunkCoordOffset;
    private Vector3d lastWorldBlockOffsetRem;

    private int recaptureAllRequested;
    private ulong[]? recaptureVirtualPageKeys;
    private int recaptureCount;
    private int recaptureCursor;

    internal bool TryGetNearDispatchState(
        out LumonScenePhysicalFieldPool nearPool,
        out LumonSceneFieldGpuResources nearFieldGpu,
        out System.Collections.Generic.IReadOnlyDictionary<uint, ulong> physicalToVirtualNear,
        out LumonScenePageTableEntry[] pageTableMirrorMip0)
    {
        nearPool = physicalPools.Near;
        nearFieldGpu = nearGpu;
        physicalToVirtualNear = physicalToVirtual;
        pageTableMirrorMip0 = pageTableMirror;

        // Require that GPU resources exist for dispatch.
        if (!configured)
        {
            return false;
        }

        if (physicalPools.Near.GpuResources is null)
        {
            return false;
        }

        return EnsureGeometryHistoryCurrent();
    }

    internal void SetTraceGeometryRenderer(TraceGeometryRenderer? occupancy)
    {
        traceGeometry = occupancy;
    }

    internal bool TryBuildNearRelightWorkFromScheduler(
        int maxPages,
        Span<LumonSceneRelightWorkGpu> workOut,
        Span<ulong> workVirtualKeysOut,
        out int workCount)
    {
        workCount = 0;

        if (!configured || !hasLastNearContexts)
        {
            return false;
        }

        if (maxPages <= 0 || workOut.IsEmpty || workVirtualKeysOut.IsEmpty)
        {
            return true;
        }

        if (nearRegionScheduler.RelightCount <= 0)
        {
            return true;
        }

        if (slotOwners.Length == 0 || pageTableMirror.Length == 0)
        {
            return false;
        }

        long nowTick = lastNearStateContext.NowTick;
        int vpc = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;

        int topK = Math.Min(64, nearRegionScheduler.RelightCount);
        WorldCellKey[] topKeysArr = ArrayPool<WorldCellKey>.Shared.Rent(topK);

        try
        {
            Span<WorldCellKey> topKeys = topKeysArr.AsSpan(0, topK);
            int got = nearRegionScheduler.CopyTopRelightKeys(topKeys);
            if (got <= 0)
            {
                return true;
            }

            topKeys = topKeys.Slice(0, got);

            for (int k = 0; k < topKeys.Length && workCount < maxPages; k++)
            {
                WorldCellKey key = topKeys[k];
                if (!nearRegionScheduler.TryGetCell(key, out LumonSceneRegionCell cell))
                {
                    continue;
                }

                if (cell.NextEligibleTick > nowTick)
                {
                    continue;
                }

                if (cell.DesiredState != WorldCellDesiredState.Active)
                {
                    continue;
                }

                if (!cell.HasAssignedSlot)
                {
                    continue;
                }

                // Avoid starving capture: if the cell still has capture backlog, prioritize capture.
                if (cell.NeedsCapturePages > 0)
                {
                    continue;
                }

                int baseIndex = checked((int)cell.ChunkSlot * vpc);
                if ((uint)baseIndex >= (uint)pageTableMirror.Length)
                {
                    continue;
                }

                int end = Math.Min(pageTableMirror.Length, baseIndex + vpc);
                for (int i = baseIndex; i < end && workCount < maxPages; i++)
                {
                    var entry = pageTableMirror[i];
                    uint physicalPageId = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry);
                    if (physicalPageId == 0u)
                    {
                        continue;
                    }

                    var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
                    if ((flags & LumonScenePageTableEntryPacking.Flags.Resident) == 0)
                    {
                        continue;
                    }

                    if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsRelight) == 0)
                    {
                        continue;
                    }

                    // Don't relight while capture is needed/in-flight.
                    if ((flags & (LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.Capturing)) != 0)
                    {
                        continue;
                    }

                    if ((flags & LumonScenePageTableEntryPacking.Flags.Relighting) != 0)
                    {
                        continue;
                    }

                    int vpage = i - baseIndex;
                    uint virtualPageIndex = (uint)vpage;

                    // v1: patchId is placeholder; use virtualPageIndex as a stable seed.
                    uint patchId = virtualPageIndex;
                    uint chunkSlot = cell.ChunkSlot;
                    workOut[workCount] = new LumonSceneRelightWorkGpu(
                        physicalPageId,
                        chunkSlot: chunkSlot,
                        patchId: patchId,
                        virtualPageIndex: virtualPageIndex);

                    workVirtualKeysOut[workCount] = LumonSceneVirtualPageKeyUtil.Pack(chunkSlot, virtualPageIndex);
                    workCount++;
                }
            }

            return true;
        }
        finally
        {
            ArrayPool<WorldCellKey>.Shared.Return(topKeysArr, clearArray: false);
        }
    }

    internal string DumpNearRegionSchedulerState(int topN)
    {
        try
        {
            return nearRegionScheduler.DumpState(topN);
        }
        catch
        {
            return "LS scheduler: (error)";
        }
    }

    internal int CopyNearRegionDebugSnapshots(Span<LumonSceneRegionCellDebugSnapshot> dst)
        => nearRegionScheduler.CopyDebugSnapshots(dst);

    internal bool TryClearNearPageFlagsMip0(uint chunkSlot, int virtualPageIndex, LumonScenePageTableEntryPacking.Flags flagsToClear)
    {
        if ((uint)virtualPageIndex >= (uint)VirtualPagesPerChunk)
        {
            return false;
        }

        int idx = checked((int)chunkSlot * VirtualPagesPerChunk + virtualPageIndex);
        if ((uint)idx >= (uint)pageTableMirror.Length)
        {
            return false;
        }

        var entry = pageTableMirror[idx];
        uint pid = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry);
        if (pid == 0)
        {
            return false;
        }

        var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
        flags &= ~flagsToClear;

        LumonScenePageTableEntry updated = LumonScenePageTableEntryPacking.Pack(pid, flags);
        pageTableMirror[idx] = updated;
        pageTableStats.ApplyEntryChange(chunkSlot, in entry, in updated);
        UploadPageTableEntryMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: virtualPageIndex, updated.Packed);
        return true;
    }

    internal bool TryClearNearPageFlagsMip0(int virtualPageIndex, LumonScenePageTableEntryPacking.Flags flagsToClear)
        => TryClearNearPageFlagsMip0(chunkSlot: 0u, virtualPageIndex, flagsToClear);

    internal bool TryGetNearDebugSamplingState(
        out Texture3D pageTableMip0,
        out Texture3D materialAtlas,
        out Texture3D irradianceAtlas,
        out int tileSizeTexels,
        out int tilesPerAxis,
        out int tilesPerAtlas)
    {
        pageTableMip0 = default!;
        materialAtlas = default!;
        irradianceAtlas = default!;
        tileSizeTexels = 0;
        tilesPerAxis = 0;
        tilesPerAtlas = 0;

        if (!configured)
        {
            return false;
        }

        var atlases = physicalPools.Near.GpuResources;
        if (atlases is null)
        {
            return false;
        }

        try
        {
            pageTableMip0 = nearGpu.PageTable.PageTableMip0;
            materialAtlas = atlases.MaterialAtlas;
            irradianceAtlas = atlases.IrradianceAtlas;
            tileSizeTexels = physicalPools.Near.Plan.TileSizeTexels;
            tilesPerAxis = physicalPools.Near.Plan.TilesPerAxis;
            tilesPerAtlas = physicalPools.Near.Plan.TilesPerAtlas;
            return pageTableMip0.IsValid && materialAtlas.IsValid && irradianceAtlas.IsValid && EnsureGeometryHistoryCurrent();
        }
        catch
        {
            return false;
        }
    }

    internal bool TryGetNearChunkSlotAndGeneration(in VectorInt3 chunkCoord, out uint chunkSlot, out ushort generation)
    {
        chunkSlot = 0;
        generation = 0;

        if (slotOwners.Length == 0 || slotGenerations.Length != slotOwners.Length)
        {
            return false;
        }

        if (!chunkToSlot.TryGetValue(chunkCoord, out uint slot))
        {
            return false;
        }

        if (slot >= (uint)slotGenerations.Length)
        {
            return false;
        }

        chunkSlot = slot;
        generation = slotGenerations[slot];
        return true;
    }

    public void NotifyAllDirty(string reason)
    {
        _ = reason;
        Interlocked.Exchange(ref recaptureAllRequested, 1);
    }

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    public LumonSceneFeedbackUpdateRenderer(ICoreClientAPI capi, VgeConfig config, GBufferManager gBufferManager, PartitionCoordinator worldPartition)
        : this(capi, config, gBufferManager, worldPartition, () => LumOnCameraState.Read(capi)) { }

    /// <summary>Creates the renderer with an explicit per-frame camera source for controlled runtime hosts.</summary>
    internal LumonSceneFeedbackUpdateRenderer(ICoreClientAPI capi, VgeConfig config, GBufferManager gBufferManager,
        PartitionCoordinator worldPartition, System.Func<LumOnCameraState?> readCamera)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.gBufferManager = gBufferManager ?? throw new ArgumentNullException(nameof(gBufferManager));
        this.readCamera = readCamera ?? throw new ArgumentNullException(nameof(readCamera));
        _ = worldPartition ?? throw new ArgumentNullException(nameof(worldPartition));

        nearRegionScheduler = new LumonSceneRegionScheduler(worldPartition, RetireNearRegion);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "vge_lumonscene_feedback");
        capi.Event.LeaveWorld += OnLeaveWorld;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Done)
        {
            return;
        }

        if (!config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled)
        {
            return;
        }

        using var renderTiming = diagnosticCaptureRender.Measure();
        captureVoxelShader?.Diagnostics.Poll();
        if (!gBufferManager.EnsureBuffers(capi.Render.FrameWidth, capi.Render.FrameHeight))
        {
            return;
        }

        int patchIdTex = gBufferManager.PatchIdTextureId;
        if (patchIdTex == 0)
        {
            return;
        }

        EnsureConfigured();
        if (!configured) return;
        if (!EnsureGeometryHistoryCurrent()) return;
        UpdateSlotWindowForNextFrame();
        UpdateWorldCoordUniformState();

        EnsurePageUsageStampCreated();

        if (!EnsureFeedbackMarkShader() || !EnsureFeedbackCompactShader())
        {
            return;
        }

        EnsureFeedbackMarkDebugCountersCreated();
        ResetFeedbackMarkDebugCounters();

        uint frameStamp = feedbackFrameStamp++;
        if (frameStamp == 0u) frameStamp = feedbackFrameStamp++; // avoid 0 as a valid stamp

        // Pass A: mark visible pages into a dedup stamp texture.
        using (feedbackMarkShader!.UseScope())
        {
            using var gpuScope = GlGpuProfiler.Instance.Scope("Feedback.MarkPages");

            feedbackMarkShader.BindDebugCounters(feedbackMarkDebugCounters);
            feedbackMarkShader.BindPatchIdGBuffer(patchIdTex);

            int genTexId = (slotGenerationTex is not null && slotGenerationTex.IsValid)
                ? slotGenerationTex.TextureId
                : 0;

            if (genTexId != 0)
            {
                feedbackMarkShader.BindChunkSlotGenerationTex(genTexId);
            }

            feedbackMarkShader.BindPageUsageStampImage(pageUsageStamp!, access: TextureAccess.ReadWrite);
            feedbackMarkShader.FrameStamp = frameStamp;

            int gx = (capi.Render.FrameWidth + 7) / 8;
            int gy = (capi.Render.FrameHeight + 7) / 8;
            GL.DispatchCompute(gx, gy, 1);
        }

        // MarkPages writes vge_pageUsageStamp via imageAtomics; CompactPages reads it via sampler fetch.
        // TextureFetchBarrierBit is required for image-write -> texture-fetch visibility.
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit);

        // Pass B: compact stamps -> bounded request list.
        nearGpu.PageRequests.Reset();
        using (feedbackCompactShader!.UseScope())
        {
            using var gpuScope = GlGpuProfiler.Instance.Scope("Feedback.CompactPages");

            feedbackCompactShader.BindPageUsageStamp(pageUsageStamp!.TextureId);
            feedbackCompactShader.BindPageTableMip0(nearGpu.PageTable.PageTableMip0.TextureId);

            feedbackCompactShader.BindRequestCounter(nearGpu.PageRequests.Counter);
            feedbackCompactShader.BindRequestsSsbo(nearGpu.PageRequests.Items);

            feedbackCompactShader.MaxRequests = (uint)nearGpu.PageRequests.CapacityItems;
            feedbackCompactShader.FrameStamp = frameStamp;

            int chunkSlotCount = nearGpu.PageTable.ChunkSlotCount;
            int totalEntries = checked(VirtualPagesPerChunk * Math.Max(1, chunkSlotCount));
            // v2: deterministic scan order (no rotation). Keep the uniform for shader compatibility.
            // Note: if the bounded request list saturates, earlier entries will win deterministically.
            feedbackCompactShader.ScanOffset = 0u;

            int gx = (totalEntries + 255) / 256;

            // v2: only emit unmapped pages. We no longer rely on the bounded request list to "keep alive" resident pages;
            // residency is bound to the chunk-slot window (pages are released on slot reassignment/world leave).
            feedbackCompactShader.CompactMode = 1u;
            GL.DispatchCompute(gx, 1, 1);
        }

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit);

        // Phase 9.1: ensure DesiredState is up to date before CPU request processing.
        UpdateNearRegionScheduler(frameStamp);

        int captureCount = ProcessRequestsCpu(
            maxRequestsToProcess: DefaultMaxRequestsPerFrame,
            maxNewAllocations: DefaultMaxNewAllocationsPerFrame);

        if (captureCount > 0)
        {
            if (DispatchVoxelCapture(captureCount))
            {
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
                FinalizeCaptureFlagsFromGpuQueue(captureCount);
            }
            else captureRetryVersion++;
        }

        // Phase 9.1: update region scheduler from latest slot assignments + page-table mirror.
        // Running this post-capture helps backlog counts converge in the same frame.
        UpdateNearRegionScheduler(frameStamp);
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Done);
        capi.Event.LeaveWorld -= OnLeaveWorld;
        OnLeaveWorld();
        pageUsageStamp?.Dispose();
        pageUsageStamp = null;
        nearGpu.Dispose();
        physicalPools.Dispose();
    }

    private void OnLeaveWorld()
    {
        ResetCaptureMeasurements();
        diagnosticCaptureAttempts = diagnosticCaptureFailures = diagnosticCaptureReadFailures = 0;
        ReleaseGeometryHistory();
        if (chunkResidency is not null)
        {
            chunkResidency.PageReleased -= OnChunkResidencyPageReleased;
            chunkResidency = null;
        }

        configured = false;
        lastPlanHash = 0;
        feedbackFrameStamp = 1u;

        feedbackMarkShader?.Dispose();
        feedbackMarkShader = null;

        feedbackCompactShader?.Dispose();
        feedbackCompactShader = null;

        captureVoxelShader?.Dispose();
        captureVoxelShader = null;

        // CPU-side virtual mappings are discarded on world leave, so the backing physical pools must return all pages
        // to their free lists. Otherwise, rejoining a world with the same pool plan would start with a "full" pool.
        physicalPools.Near.ResetResidency();
        physicalPools.Far.ResetResidency();

        Array.Clear(pageTableMirror);
        virtualToPhysical.Clear();
        physicalToVirtual.Clear();

        Array.Clear(slotOwners);
        chunkToSlot.Clear();
        Array.Clear(slotGenerations);
        slotGenerationTex?.Dispose();
        slotGenerationTex = null;
        slotInfoBuffer?.Dispose();
        slotInfoBuffer = null;

        slotOriginMinChunk = default;
        slotDims = default;
        slotRing = default;
        lastAnchorChunk = default;
        LumonSceneChunkSlotUniformState.Disable();
        LumonSceneWorldCoordUniformState.Disable();

        feedbackMarkDebugCounters?.Dispose();
        feedbackMarkDebugCounters = null;

        ResetRecaptureList();

        nearRegionScheduler.Reset(nowTick: 0);
        lastRegionCells = 0;
        lastRegionActive = 0;
        lastRegionLoaded = 0;
        lastRegionDesiredUnloaded = 0;
        lastRegionDesiredLoaded = 0;
        lastRegionDesiredActive = 0;
        lastRegionSuppressed = 0;
        lastLoadedWindowSize = 0;
        lastActiveWindowSize = 0;
        lastBudgetMaxRequests = 0;
        lastBudgetMaxNewAllocs = 0;
        lastBudgetMaxRelightPages = 0;
        hasLastNearContexts = false;
    }

    private void UpdateNearRegionScheduler(uint frameStamp)
    {
        // Refresh domain metadata before the coordinator evaluates slot residency.
        nearRegionScheduler.SetNowTick(unchecked((long)frameStamp));

        if (!configured || slotDims == default || slotGenerations.Length == 0 || pageTableMirror.Length == 0)
        {
            lastRegionCells = 0;
            lastRegionActive = 0;
            lastRegionLoaded = 0;
            lastRegionDesiredUnloaded = 0;
            lastRegionDesiredLoaded = 0;
            lastRegionDesiredActive = 0;
            lastRegionSuppressed = 0;
            lastLoadedWindowSize = 0;
            lastActiveWindowSize = 0;
            lastBudgetMaxRequests = 0;
            lastBudgetMaxNewAllocs = 0;
            lastBudgetMaxRelightPages = 0;
            return;
        }

        VectorInt3 loadedMin = slotOriginMinChunk;
        VectorInt3 loadedMax = slotOriginMinChunk + new VectorInt3(slotDims.X - 1, slotDims.Y - 1, slotDims.Z - 1);

        // v1: active window is a tighter box than the loaded (slot) window to provide hysteresis.
        // Heat can promote Loaded→Active outside the active window.
        int rXZ = Math.Max(0, config.LumOn.LumonScene.NearRadiusChunks);
        int rY = Math.Max(0, config.LumOn.LumonScene.NearRadiusYChunks);
        int arXZ = Math.Max(0, rXZ - 1);
        int arY = Math.Max(0, rY - 1);

        VectorInt3 activeMin = new VectorInt3(lastAnchorChunk.X - arXZ, lastAnchorChunk.Y - arY, lastAnchorChunk.Z - arXZ);
        VectorInt3 activeMax = new VectorInt3(lastAnchorChunk.X + arXZ, lastAnchorChunk.Y + arY, lastAnchorChunk.Z + arXZ);

        lastLoadedWindowSize = slotOwners.Length;
        lastActiveWindowSize = checked((arXZ * 2 + 1) * (arY * 2 + 1) * (arXZ * 2 + 1));
        lastBudgetMaxRequests = DefaultMaxRequestsPerFrame;
        lastBudgetMaxNewAllocs = DefaultMaxNewAllocationsPerFrame;
        lastBudgetMaxRelightPages = Math.Clamp(config.LumOn.LumonScene.RelightSeedPagesPerFrame, 0, 256) +
            Math.Clamp(config.LumOn.LumonScene.RelightDirectPagesPerFrame, 0, 256) +
            Math.Clamp(config.LumOn.LumonScene.RelightIndirectPagesPerFrame, 0, 256);

        VectorInt3 anchorBlock = new VectorInt3(lastAnchorChunk.X * 32, lastAnchorChunk.Y * 32, lastAnchorChunk.Z * 32);

        var stateContext = new WorldCellStateTransitionContext(
            CameraBlockPos: anchorBlock,
            AnchorBlockPos: anchorBlock,
            HasAnchor: true,
            LoadedWindowMinRegion: loadedMin,
            LoadedWindowMaxRegion: loadedMax,
            HasLoadedWindow: true,
            ActiveWindowMinRegion: activeMin,
            ActiveWindowMaxRegion: activeMax,
            HasActiveWindow: true,
            NowTick: unchecked((long)frameStamp));

        var priorityContext = new WorldCellPriorityContext(
            CameraBlockPos: anchorBlock,
            AnchorBlockPos: anchorBlock,
            HasAnchor: true,
            WindowMinRegion: loadedMin,
            WindowMaxRegion: loadedMax,
            HasWindow: true,
            NowTick: unchecked((long)frameStamp),
            IsCellLikelyLoaded: null);

        lastNearStateContext = stateContext;
        lastNearPriorityContext = priorityContext;
        hasLastNearContexts = true;

        var keep = new System.Collections.Generic.HashSet<WorldCellKey>(capacity: Math.Max(16, chunkToSlot.Count));

        int active = 0;
        int loaded = 0;
        int desiredActive = 0;
        int desiredLoaded = 0;
        int desiredUnloaded = 0;
        int suppressed = 0;

        foreach (var kvp in chunkToSlot)
        {
            VectorInt3 owner = kvp.Key;
            uint slot = kvp.Value;
            if (slot >= (uint)slotGenerations.Length)
            {
                continue;
            }

            var coord = new LumonSceneChunkCoord(owner.X, owner.Y, owner.Z);
            LumonSceneRegionCell cell = nearRegionScheduler.GetOrCreate(WorldCellKind.LumonSceneNear, in coord);
            keep.Add(cell.Key);

            nearRegionScheduler.UpdateSlot(cell, slot, slotGenerations[slot], unchecked((long)frameStamp));
            cell.UpdateBacklogFromSlotStats(pageTableStats.GetSlot(slot));


            if (cell.NextEligibleTick > stateContext.NowTick) suppressed++;

        }

        // Acknowledge slot residency before reporting desired versus actual coverage.
        nearRegionScheduler.UpdateResidency(in stateContext, in priorityContext);
        foreach (WorldCellKey key in keep)
        {
            if (!nearRegionScheduler.TryGetCell(key, out LumonSceneRegionCell cell)) continue;
            switch (cell.DesiredState)
            {
                case WorldCellDesiredState.Active: desiredActive++; break;
                case WorldCellDesiredState.Loaded: desiredLoaded++; break;
                default: desiredUnloaded++; break;
            }
            if (cell.ActualState == WorldCellActualState.Active) active++;
            if (cell.ActualState is WorldCellActualState.Active or WorldCellActualState.Loaded) loaded++;
        }
        lastRegionCells = keep.Count;
        lastRegionActive = active;
        lastRegionLoaded = loaded;
        lastRegionDesiredActive = desiredActive;
        lastRegionDesiredLoaded = desiredLoaded;
        lastRegionDesiredUnloaded = desiredUnloaded;
        lastRegionSuppressed = suppressed;
    }

    private void EnsureConfigured()
    {
        var cfg = config.LumOn.LumonScene;

        // v1: Near field only (chunkSlotCount=1). Far field integration comes later.
        physicalPools.ConfigureFrom(cfg, maxAtlasCount: cfg.MaxAtlasCount);

        int planHash = HashCode.Combine(
            cfg.NearTexelsPerVoxelFaceEdge,
            cfg.NearRadiusChunks,
            cfg.NearRadiusYChunks,
            cfg.NearPagesPerChunkBudget,
            physicalPools.Near.Plan.CapacityPages);

        if (configured && planHash == lastPlanHash)
        {
            return;
        }

        lastPlanHash = planHash;
        configured = true;

        int chunkSlotCount = Math.Max(1, LumonScenePoolSizingUtil.ComputeChunkSlotCountBoxField(cfg.NearRadiusChunks, cfg.NearRadiusYChunks));

        // Allocate Near GPU resources sized to the physical capacity.
        nearGpu.Configure(chunkSlotCount: chunkSlotCount, physicalPageCapacity: Math.Max(1, physicalPools.Near.Plan.CapacityPages));
        nearGpu.EnsureCreated();

        physicalPools.EnsureGpuResources();
        if (physicalPools.Near.GpuResources is null)
        {
            // Defer publication until another live pool releases enough total-byte credit.
            configured = false;
            return;
        }
        EnsurePageUsageStampCreated();

        if (chunkResidency is not null)
        {
            chunkResidency.PageReleased -= OnChunkResidencyPageReleased;
            chunkResidency = null;
        }

        chunkResidency = new LumonSceneChunkResidencyManager(physicalPools);
        chunkResidency.PageReleased += OnChunkResidencyPageReleased;

        pageTableMirror = new LumonScenePageTableEntry[checked(VirtualPagesPerChunk * chunkSlotCount)];
        virtualToPhysical = new System.Collections.Generic.Dictionary<ulong, uint>(capacity: Math.Max(16, chunkSlotCount));
        physicalToVirtual = new System.Collections.Generic.Dictionary<uint, ulong>(capacity: Math.Max(16, chunkSlotCount));
        pageTableStats.Reset(chunkSlotCount);

        EnsureSlotStateConfigured(chunkSlotCount);
        UpdateWorldCoordUniformState();

        cpuProcessor = new LumonSceneFeedbackRequestProcessor(
            physicalPools.Near,
            pageTableMirror,
            virtualToPhysical,
            physicalToVirtual,
            new RendererPageTableWriter(this),
            pageTableStats,
            TryAdmitCapture);

        ResetRecaptureList();

        ClearCaptureAdmission();

        // Best-effort clear GPU page table to zeros.
        nearGpu.PageTable.EnsureCreated();

        EnsureFeedbackMarkDebugCountersCreated();
    }

    private void EnsureFeedbackMarkDebugCountersCreated()
    {
        if (feedbackMarkDebugCounters is not null && feedbackMarkDebugCounters.IsValid)
        {
            return;
        }

        feedbackMarkDebugCounters?.Dispose();
        feedbackMarkDebugCounters = GpuAtomicCounterBuffer.Create(
            usage: BufferUsageHint.DynamicRead,
            debugName: "LumOn.LumonScene.Feedback.MarkCounters(ACBO)");
        feedbackMarkDebugCounters.InitializeCounters(counterCount: FeedbackMarkDebugCounterCount, initialValue: 0u);
    }

    private void ResetFeedbackMarkDebugCounters()
    {
        if (feedbackMarkDebugCounters is null || !feedbackMarkDebugCounters.IsValid)
        {
            return;
        }

        Span<uint> zero = stackalloc uint[FeedbackMarkDebugCounterCount] { 0u, 0u, 0u };
        feedbackMarkDebugCounters.UploadSubData((ReadOnlySpan<uint>)zero, dstOffsetBytes: 0);
    }

    private void EnsureSlotStateConfigured(int chunkSlotCount)
    {
        // v1: 3D chunkSlot window with core dims = (2*R+1) per axis (R is Chebyshev radius in chunk coords).
        int rXZ = Math.Max(0, config.LumOn.LumonScene.NearRadiusChunks);
        int rY = Math.Max(0, config.LumOn.LumonScene.NearRadiusYChunks);
        int dimX = checked(rXZ * 2 + 1);
        int dimY = checked(rY * 2 + 1);
        int dimZ = dimX;

        if (checked(dimX * dimY * dimZ) != chunkSlotCount)
        {
            slotDims = default;
            slotOriginMinChunk = default;
            slotRing = default;
            slotOwners = Array.Empty<VectorInt3>();
            chunkToSlot = new System.Collections.Generic.Dictionary<VectorInt3, uint>();
            slotGenerations = Array.Empty<ushort>();
            slotGenerationTex?.Dispose();
            slotGenerationTex = null;
            slotInfoBuffer?.Dispose();
            slotInfoBuffer = null;
            LumonSceneChunkSlotUniformState.Disable();
            capi.Logger.Warning(
                "[VGE] LumonScene: cannot configure chunkSlot window dims for chunkSlotCount={0} (NearRadiusChunks={1}, NearRadiusYChunks={2}). Mapping disabled.",
                chunkSlotCount,
                rXZ,
                rY);
            return;
        }

        slotDims = new VectorInt3(dimX, dimY, dimZ);
        slotRing = default;
        slotOwners = new VectorInt3[chunkSlotCount];
        chunkToSlot = new System.Collections.Generic.Dictionary<VectorInt3, uint>(capacity: Math.Max(16, chunkSlotCount));
        slotGenerations = new ushort[chunkSlotCount];

        slotGenerationTex?.Dispose();
        slotGenerationTex = Texture2D.Create(
            width: chunkSlotCount,
            height: 1,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            debugName: $"LumOn.LumonScene.{LumonSceneField.Near}.ChunkSlotGeneration(R32UI)");

        // Clear to zero once (generation starts at 0). We'll upload per-slot updates as they change.
        slotGenerationTex.UploadDataImmediate(new uint[checked(chunkSlotCount)], x: 0, y: 0, regionWidth: chunkSlotCount, regionHeight: 1);

        slotInfoBuffer?.Dispose();
        slotInfoBuffer = new LumonSceneChunkSlotInfoGpuBuffer(LumonSceneField.Near, capacityEntries: chunkSlotCount);
        slotInfoBuffer.EnsureCreated();

        // Seed window from current anchor (will take effect next frame).
        if (TryGetAnchorChunkCoord(out VectorInt3 anchorChunk))
        {
            lastAnchorChunk = anchorChunk;
            slotOriginMinChunk = ComputeOriginMinChunkForAnchor(anchorChunk);
        }
        else
        {
            lastAnchorChunk = default;
            slotOriginMinChunk = default;
        }

        RebuildSlotAssignmentsAndRecycleReassignedSlots(forceRecycleAll: true);
        LumonSceneChunkSlotUniformState.Update(slotOriginMinChunk, slotDims, slotRing, slotGenerationTex.TextureId);
    }

    private bool TryGetAnchorChunkCoord(out VectorInt3 chunkCoord)
    {
        chunkCoord = default;
        try
        {
            if (readCamera() is not { } camera)
            {
                return false;
            }

            // Entity positions are in block units.
            double x = camera.PositionX;
            double y = camera.PositionY;
            double z = camera.PositionZ;

            int cx = (int)Math.Floor(x * (1.0 / 32.0));
            int cy = (int)Math.Floor(y * (1.0 / 32.0));
            int cz = (int)Math.Floor(z * (1.0 / 32.0));
            chunkCoord = new VectorInt3(cx, cy, cz);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private VectorInt3 ComputeOriginMinChunkForAnchor(VectorInt3 anchorChunk)
    {
        int rXZ = Math.Max(0, config.LumOn.LumonScene.NearRadiusChunks);
        int rY = Math.Max(0, config.LumOn.LumonScene.NearRadiusYChunks);

        return new VectorInt3(
            anchorChunk.X - rXZ,
            anchorChunk.Y - rY,
            anchorChunk.Z - rXZ);
    }

    private void UpdateSlotWindowForNextFrame()
    {
        if (slotDims == default || slotGenerationTex is null || !slotGenerationTex.IsValid)
        {
            return;
        }

        if (!TryGetAnchorChunkCoord(out VectorInt3 anchorChunk))
        {
            return;
        }

        if (anchorChunk == lastAnchorChunk)
        {
            return;
        }

        VectorInt3 newOrigin = ComputeOriginMinChunkForAnchor(anchorChunk);

        // Let the coordinator retire departing identities before the backend recycles their slots.
        int activeXZ = Math.Max(0, config.LumOn.LumonScene.NearRadiusChunks - 1);
        int activeY = Math.Max(0, config.LumOn.LumonScene.NearRadiusYChunks - 1);
        var nextCoverage = lastNearStateContext with
        {
            LoadedWindowMinRegion = newOrigin,
            LoadedWindowMaxRegion = newOrigin + slotDims - new VectorInt3(1, 1, 1),
            HasLoadedWindow = true,
            ActiveWindowMinRegion = anchorChunk - new VectorInt3(activeXZ, activeY, activeXZ),
            ActiveWindowMaxRegion = anchorChunk + new VectorInt3(activeXZ, activeY, activeXZ),
            HasActiveWindow = true
        };
        nearRegionScheduler.UpdateCoverage(nextCoverage);
        VectorInt3 delta = newOrigin - slotOriginMinChunk;
        slotOriginMinChunk = newOrigin;

        // ring = (ring + delta) mod dims (per-axis).
        slotRing = new VectorInt3(
            ModPositive(slotRing.X + delta.X, slotDims.X),
            ModPositive(slotRing.Y + delta.Y, slotDims.Y),
            ModPositive(slotRing.Z + delta.Z, slotDims.Z));

        lastAnchorChunk = anchorChunk;

        RebuildSlotAssignmentsAndRecycleReassignedSlots(forceRecycleAll: false);
        LumonSceneChunkSlotUniformState.Update(slotOriginMinChunk, slotDims, slotRing, slotGenerationTex.TextureId);
    }

    /// <summary>Retires GPU page mappings only after the coordinator relinquishes this logical owner.</summary>
    private void RetireNearRegion(LumonSceneChunkCoord coordinate)
    {
        var owner = new VectorInt3(coordinate.X, coordinate.Y, coordinate.Z);
        chunkResidency?.OnChunkUnloaded(coordinate);
        if (chunkToSlot.TryGetValue(owner, out uint slot) && slot < (uint)slotOwners.Length && slotOwners[slot] == owner)
            ClearSlotResidency(slot);
    }
    private void OnChunkResidencyPageReleased(LumonScenePageReleasedEvent e)
    {
        // Eviction/unload safety: if a physical page is released, clear any virtual-page mapping that still references it.
        // Also downgrade the owning cell's ActualState so we don't keep issuing work for stale content.

        if (e.Field != LumonSceneField.Near)
        {
            return;
        }

        if (e.PhysicalPageId != 0 && physicalToVirtual.TryGetValue(e.PhysicalPageId, out ulong key))
        {
            physicalToVirtual.Remove(e.PhysicalPageId);
            virtualToPhysical.Remove(key);

            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
            int vpage = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);

            int mirrorIndex = checked((int)slot * VirtualPagesPerChunk + vpage);
            if ((uint)mirrorIndex < (uint)pageTableMirror.Length)
            {
                LumonScenePageTableEntry oldEntry = pageTableMirror[mirrorIndex];
                LumonScenePageTableEntry newEntry = default;
                pageTableMirror[mirrorIndex] = newEntry;
                pageTableStats.ApplyEntryChange(slot, in oldEntry, in newEntry);
            }

            UploadPageTableEntryMip0(chunkSlot: (int)slot, virtualPageIndex: vpage, packedEntry: 0u);
        }

        if (hasLastNearContexts)
        {
            WorldCellKey cellKey = WorldCellKey.FromLumonSceneNear(e.Chunk.ToKey());
            if (nearRegionScheduler.TryGetCell(cellKey, out LumonSceneRegionCell cell))
            {

                // Cool down a bit to avoid immediate thrash if the pool is under pressure.
                cell.NextEligibleTick = Math.Max(cell.NextEligibleTick, lastNearStateContext.NowTick + 30);
                cell.EnqueueStateAndWork(nearRegionScheduler, in lastNearPriorityContext);
            }
        }
    }

    /// <summary>Publishes the terrain render origin shared by PatchIds and cache feedback.</summary>
    private void UpdateWorldCoordUniformState()
    {
        try
        {
            if (readCamera() is not { } camera)
            {
                return;
            }

            // Match ChunkRenderer's CameraPos-relative vertices. Inverse-view translation
            // is already handled by reconstruction and must not be subtracted here.
            var (chunkOffset, blockRemainder) = LumOnFrameWorldSpaceBridge.Compute(
                camera.CameraX, camera.CameraY, camera.CameraZ);
            LumonSceneWorldCoordUniformState.Update(chunkOffset, blockRemainder);
            lastWorldChunkCoordOffset = chunkOffset;
            lastWorldBlockOffsetRem = blockRemainder;
        }
        catch
        {
            // Best-effort: leave last values.
        }
    }

    private void RebuildSlotAssignmentsAndRecycleReassignedSlots(bool forceRecycleAll)
    {
        if (slotOwners.Length == 0 || slotGenerations.Length != slotOwners.Length)
        {
            return;
        }

        chunkToSlot.Clear();

        int dimX = slotDims.X;
        int dimY = slotDims.Y;
        int dimZ = slotDims.Z;
        int ringX = slotRing.X;
        int ringY = slotRing.Y;
        int ringZ = slotRing.Z;

        for (uint slot = 0; slot < (uint)slotOwners.Length; slot++)
        {
            // Inverse mapping of: outChunkSlot = (sy * dimZ + sz) * dimX + sx
            int sx = (int)(slot % (uint)dimX);
            uint tmp = slot / (uint)dimX;
            int sz = (int)(tmp % (uint)dimZ);
            int sy = (int)(tmp / (uint)dimZ);

            int localX = ModPositive(sx - ringX, dimX);
            int localY = ModPositive(sy - ringY, dimY);
            int localZ = ModPositive(sz - ringZ, dimZ);

            VectorInt3 owner = slotOriginMinChunk + new VectorInt3(localX, localY, localZ);

            if (forceRecycleAll || slotOwners[slot] != owner)
            {
                ReassignSlot(slot, owner);
            }
            else
            {
                chunkToSlot[owner] = slot;
            }
        }
    }

    private void ReassignSlot(uint slot, VectorInt3 newOwner)
    {
        // Clear any resident pages for this slot and bump generation so stale PatchId pixels are rejected.
        ClearSlotResidency(slot);
        slotGenerations[slot] = unchecked((ushort)(slotGenerations[slot] + 1));

        if (slotGenerationTex is not null && slotGenerationTex.IsValid)
        {
            uint gen = slotGenerations[slot];
            slotGenerationTex.UploadDataImmediate(new[] { gen }, x: (int)slot, y: 0, regionWidth: 1, regionHeight: 1);
        }

        if (slotInfoBuffer is not null)
        {
            int ox = unchecked(newOwner.X * 32);
            int oy = unchecked(newOwner.Y * 32);
            int oz = unchecked(newOwner.Z * 32);

            var info = new LumonSceneChunkSlotInfoGpu(
                ChunkOriginBlocksAndGeneration: new VectorInt4(ox, oy, oz, slotGenerations[slot]));

            Span<LumonSceneChunkSlotInfoGpu> one = stackalloc LumonSceneChunkSlotInfoGpu[1] { info };
            slotInfoBuffer.Ssbo.UploadSubData((ReadOnlySpan<LumonSceneChunkSlotInfoGpu>)one, dstOffsetBytes: checked((int)slot * System.Runtime.InteropServices.Marshal.SizeOf<LumonSceneChunkSlotInfoGpu>()));
        }

        slotOwners[slot] = newOwner;
        chunkToSlot[newOwner] = slot;
    }

    private void ClearSlotResidency(uint chunkSlot)
    {
        if (!configured)
        {
            return;
        }

        if (virtualToPhysical.Count <= 0)
        {
            return;
        }

        int rent = Math.Max(1, virtualToPhysical.Count);
        ulong[] keys = ArrayPool<ulong>.Shared.Rent(rent);
        int count = 0;

        try
        {
            foreach (var kvp in virtualToPhysical)
            {
                if (LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(kvp.Key) == chunkSlot)
                {
                    keys[count++] = kvp.Key;
                }
            }

            for (int i = 0; i < count; i++)
            {
                ulong key = keys[i];
                if (!virtualToPhysical.TryGetValue(key, out uint physicalPageId))
                {
                    continue;
                }

                virtualToPhysical.Remove(key);
                physicalToVirtual.Remove(physicalPageId);

                int vpage = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
                int mirrorIndex = checked((int)chunkSlot * VirtualPagesPerChunk + vpage);
                if ((uint)mirrorIndex < (uint)pageTableMirror.Length)
                {
                    LumonScenePageTableEntry oldEntry = pageTableMirror[mirrorIndex];
                    LumonScenePageTableEntry newEntry = default;
                    pageTableMirror[mirrorIndex] = newEntry;
                    pageTableStats.ApplyEntryChange(chunkSlot, in oldEntry, in newEntry);
                }

                UploadPageTableEntryMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: vpage, packedEntry: 0u);

                physicalPools.Near.Free(physicalPageId);
            }
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(keys, clearArray: false);
        }
    }

    private static int ModPositive(int v, int m)
    {
        if (m <= 0) return 0;
        int r = v % m;
        return (r < 0) ? (r + m) : r;
    }

    private void EnsurePageUsageStampCreated()
    {
        if (pageUsageStamp is not null && pageUsageStamp.IsValid && pageUsageStamp.Depth == nearGpu.PageTable.ChunkSlotCount)
        {
            return;
        }

        pageUsageStamp?.Dispose();
        pageUsageStamp = Texture3D.Create(
            width: LumonSceneVirtualAtlasConstants.VirtualPageTableWidth,
            height: LumonSceneVirtualAtlasConstants.VirtualPageTableHeight,
            depth: Math.Max(1, nearGpu.PageTable.ChunkSlotCount),
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: $"LumOn.LumonScene.{LumonSceneField.Near}.PageUsageStamp(R32UI)");

        // Clear to 0 once; stamps use frameStamp!=0 to avoid needing per-frame clears.
        _ = pageUsageStamp.TryClearToZero();
    }

    private bool EnsureFeedbackMarkShader()
    {
        if (feedbackMarkShader is not null && feedbackMarkShader.IsValid)
        {
            return true;
        }

        feedbackMarkShader?.Dispose();
        feedbackMarkShader = null;

        if (!LumonSceneFeedbackMarkPagesComputeShader.TryCreate(
            api: capi,
            shader: out feedbackMarkShader,
            infoLog: out string infoLog,
            preferSpirv: true,
            debugName: "LumOn.LumonScene.FeedbackMarkPages"))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene feedback mark compute shader: {0}", infoLog);
            feedbackMarkShader = null;
            return false;
        }

        return feedbackMarkShader is not null;
    }

    private bool EnsureFeedbackCompactShader()
    {
        if (feedbackCompactShader is not null && feedbackCompactShader.IsValid)
        {
            return true;
        }

        feedbackCompactShader?.Dispose();
        feedbackCompactShader = null;

        if (!LumonSceneFeedbackCompactPagesComputeShader.TryCreate(
            api: capi,
            shader: out feedbackCompactShader,
            infoLog: out string infoLog,
            preferSpirv: true,
            debugName: "LumOn.LumonScene.FeedbackCompactPages"))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene feedback compact compute shader: {0}", infoLog);
            feedbackCompactShader = null;
            return false;
        }

        return feedbackCompactShader is not null;
    }

    private bool EnsureCaptureVoxelShader()
    {
        if (captureVoxelShader is not null && captureVoxelShader.IsValid)
        {
            return true;
        }

        captureVoxelShader?.Dispose();
        captureVoxelShader = null;

        if (!LumonSceneCaptureVoxelComputeShader.TryCreate(
            api: capi,
            shader: out captureVoxelShader,
            infoLog: out string infoLog,
            preferSpirv: true,
            debugName: "LumOn.LumonScene.CaptureVoxel"))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene capture voxel compute shader: {0}", infoLog);
            captureVoxelShader = null;
            return false;
        }

        return captureVoxelShader is not null;
    }

    private int ProcessRequestsCpu(int maxRequestsToProcess, int maxNewAllocations)
    {
        lastCaptureCount = lastRelightCount = 0;
        EnsureRecaptureListIfRequested();

        uint requestCount;
        using (var mapped = nearGpu.PageRequests.Counter.MapRange<uint>(dstOffsetBytes: 0, elementCount: 1, access: MapBufferAccessMask.MapReadBit))
        {
            requestCount = (mapped.IsMapped && mapped.Span.Length > 0) ? mapped.Span[0] : 0u;
        }

        int available = nearGpu.PageRequests.CapacityItems;
        int toRead = (int)Math.Min(requestCount, (uint)available);
        int processBudget = Math.Max(0, maxRequestsToProcess);
        int toProcess = Math.Min(toRead, processBudget);
        int maxRecapture = 8;
        int maxCapture = Math.Max(1, maxNewAllocations) + maxRecapture;

        if (toRead <= 0 && recaptureVirtualPageKeys is null)
        {
            nearGpu.CaptureWork.Reset();
            nearGpu.RelightWork.Reset();
            return 0;
        }

        LumonScenePageRequestGpu[] scratch = ArrayPool<LumonScenePageRequestGpu>.Shared.Rent(Math.Max(1, toRead));
        LumonSceneCaptureWorkGpu[] captureScratch = ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Rent(maxCapture);
        LumonSceneRelightWorkGpu[] relightScratch = ArrayPool<LumonSceneRelightWorkGpu>.Shared.Rent(maxCapture);

        ushort[]? slotNewAllocsArr = null;
        ushort[]? slotStarvedBudgetArr = null;
        ushort[]? slotStarvedChunkBudgetArr = null;
        ushort[]? slotAllocFailuresArr = null;

        Span<ushort> slotNewAllocs = Span<ushort>.Empty;
        Span<ushort> slotStarvedBudget = Span<ushort>.Empty;
        Span<ushort> slotStarvedChunkBudget = Span<ushort>.Empty;
        Span<ushort> slotAllocFailures = Span<ushort>.Empty;

        try
        {
            lastRequestCount = requestCount;
            lastRequestsRead = toRead;
            lastRequestsProcessed = toProcess;
            lastRequestsDroppedGpu = requestCount <= (uint)toRead ? 0 : (int)Math.Min(int.MaxValue, requestCount - (uint)toRead);
            lastRequestsDroppedCpuBudget = 0;
            lastRequestsDroppedInactive = 0;
            lastRequestsPrioritized = 0;
            lastPrioritizeTopK = 0;
            lastUniqueVirtualPages = 0;
            lastTopChunkSlot = 0u;
            lastTopVirtualPage = 0;
            lastTopVirtualPageCount = 0;
            lastMarkRejectPatchId0 = 0u;
            lastMarkRejectChunkSlotOob = 0u;
            lastMarkRejectGenMismatch = 0u;

            if (toRead > 0)
            {
                using (var mappedItems = nearGpu.PageRequests.Items.MapRange<LumonScenePageRequestGpu>(
                    dstOffsetBytes: 0,
                    elementCount: toRead,
                    access: MapBufferAccessMask.MapReadBit))
                {
                    if (!mappedItems.IsMapped)
                    {
                        return 0;
                    }

                    mappedItems.Span.CopyTo(scratch);
                }
            }

            // Phase 9.1: heat signal from raw GPU requests.
            // We apply this before filtering so a briefly-visible chunk can become Active in the same frame.
            if (toRead > 0 && hasLastNearContexts && slotOwners.Length > 0)
            {
                int slotCount = slotOwners.Length;
                int[] heatCounts = ArrayPool<int>.Shared.Rent(slotCount);
                uint[] changed = ArrayPool<uint>.Shared.Rent(Math.Min(toRead, slotCount));
                int changedCount = 0;

                try
                {
                    Array.Clear(heatCounts, 0, slotCount);

                    for (int i = 0; i < toRead; i++)
                    {
                        uint slot = scratch[i].ChunkSlot;
                        if (slot >= (uint)slotCount)
                        {
                            continue;
                        }

                        if (heatCounts[(int)slot] == 0)
                        {
                            changed[changedCount++] = slot;
                        }

                        heatCounts[(int)slot]++;
                    }

                    long nowTick = lastNearStateContext.NowTick;

                    for (int i = 0; i < changedCount; i++)
                    {
                        uint slot = changed[i];
                        VectorInt3 owner = slotOwners[slot];
                        var coord = new LumonSceneChunkCoord(owner.X, owner.Y, owner.Z);
                        WorldCellKey key = WorldCellKey.FromLumonSceneNear(coord.ToKey());

                        if (!nearRegionScheduler.TryGetCell(key, out LumonSceneRegionCell cell))
                        {
                            continue;
                        }

                        cell.ApplyHeatFromRequests(heatCounts[(int)slot], nowTick);

                        cell.EnqueueStateAndWork(nearRegionScheduler, in lastNearPriorityContext);
                    }
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(heatCounts, clearArray: false);
                    ArrayPool<uint>.Shared.Return(changed, clearArray: false);
                }
            }

            nearRegionScheduler.UpdateResidency(in lastNearStateContext, in lastNearPriorityContext);
            // Accept requests for any cell that is within the Loaded window (Loaded or Active). Unloaded cells are ignored.
            if (toRead > 0 && slotOwners.Length > 0 && slotGenerations.Length == slotOwners.Length)
            {
                int write = 0;
                for (int i = 0; i < toRead; i++)
                {
                    uint slot = scratch[i].ChunkSlot;
                    if (slot >= (uint)slotOwners.Length)
                    {
                        lastRequestsDroppedInactive++;
                        continue;
                    }

                    VectorInt3 owner = slotOwners[slot];
                    var coord = new LumonSceneChunkCoord(owner.X, owner.Y, owner.Z);
                    WorldCellKey key = WorldCellKey.FromLumonSceneNear(coord.ToKey());

                    if (nearRegionScheduler.TryGetCell(key, out LumonSceneRegionCell cell)
                        && cell.DesiredState != WorldCellDesiredState.Unloaded
                        && (!hasLastNearContexts || cell.NextEligibleTick <= lastNearStateContext.NowTick))
                    {
                        scratch[write++] = scratch[i];
                    }
                    else
                    {
                        lastRequestsDroppedInactive++;
                    }
                }

                // Apply CPU processing budget after gating.
                lastRequestsDroppedCpuBudget = Math.Max(0, write - toProcess);
                toProcess = Math.Min(write, toProcess);
                lastRequestsProcessed = toProcess;
            }
            else
            {
                // No gating applied; budget is still the limiter.
                lastRequestsDroppedCpuBudget = Math.Max(0, toRead - toProcess);
            }

            // Phase 9.1: scheduler-driven selection. Prioritize requests belonging to the highest-priority capture cells.
            // cpuProcessor.Process consumes requests in-order; since budgets are capped, earlier requests win.
            if (toProcess > 1 && slotOwners.Length > 0 && nearRegionScheduler.CaptureCount > 0)
            {
                int topK = Math.Min(32, nearRegionScheduler.CaptureCount);
                WorldCellKey[] topKeysArr = ArrayPool<WorldCellKey>.Shared.Rent(topK);

                try
                {
                    Span<WorldCellKey> topKeys = topKeysArr.AsSpan(0, topK);
                    int got = nearRegionScheduler.CopyTopCaptureKeys(topKeys);
                    if (got > 0)
                    {
                        topKeys = topKeys.Slice(0, got);

                        // Build per-slot rank map: lower = higher priority, int.MaxValue = not prioritized.
                        int slotCount = slotOwners.Length;
                        int[] slotRank = ArrayPool<int>.Shared.Rent(slotCount);
                        try
                        {
                            Array.Fill(slotRank, int.MaxValue, 0, slotCount);

                            for (int r = 0; r < topKeys.Length; r++)
                            {
                                WorldCellKey key = topKeys[r];
                                LumonSceneChunkCoord coord = LumonSceneChunkCoord.FromKey(key.Packed);
                                var owner = new VectorInt3(coord.X, coord.Y, coord.Z);

                                if (!chunkToSlot.TryGetValue(owner, out uint slot) || slot >= (uint)slotCount)
                                {
                                    continue;
                                }

                                int existing = slotRank[(int)slot];
                                if (r < existing)
                                {
                                    slotRank[(int)slot] = r;
                                }
                            }

                            // Stable bucket reorder into K+1 buckets (topK ranks + remainder).
                            LumonScenePageRequestGpu[] reordered = ArrayPool<LumonScenePageRequestGpu>.Shared.Rent(toProcess);
                            try
                            {
                                Span<int> counts = stackalloc int[33];
                                counts.Clear();

                                int bucketCount = Math.Min(32, topKeys.Length) + 1;
                                int restBucket = bucketCount - 1;

                                lastPrioritizeTopK = restBucket;

                                for (int i = 0; i < toProcess; i++)
                                {
                                    uint slot = scratch[i].ChunkSlot;
                                    int rank = (slot < (uint)slotCount) ? slotRank[(int)slot] : int.MaxValue;
                                    int b = (rank >= 0 && rank < restBucket) ? rank : restBucket;
                                    counts[b]++;
                                }

                                int prioritized = 0;
                                for (int b = 0; b < restBucket; b++)
                                {
                                    prioritized += counts[b];
                                }

                                lastRequestsPrioritized = prioritized;

                                Span<int> starts = stackalloc int[33];
                                starts.Clear();
                                int sum = 0;
                                for (int b = 0; b < bucketCount; b++)
                                {
                                    starts[b] = sum;
                                    sum += counts[b];
                                }

                                for (int i = 0; i < toProcess; i++)
                                {
                                    uint slot = scratch[i].ChunkSlot;
                                    int rank = (slot < (uint)slotCount) ? slotRank[(int)slot] : int.MaxValue;
                                    int b = (rank >= 0 && rank < restBucket) ? rank : restBucket;
                                    int dst = starts[b]++;
                                    reordered[dst] = scratch[i];
                                }

                                reordered.AsSpan(0, toProcess).CopyTo(scratch);
                            }
                            finally
                            {
                                ArrayPool<LumonScenePageRequestGpu>.Shared.Return(reordered, clearArray: false);
                            }
                        }
                        finally
                        {
                            ArrayPool<int>.Shared.Return(slotRank, clearArray: false);
                        }
                    }
                }
                finally
                {
                    ArrayPool<WorldCellKey>.Shared.Return(topKeysArr, clearArray: false);
                }
            }

            if (config.Debug.LumOnRuntimeSelfCheckEnabled)
            {
                if (feedbackMarkDebugCounters is not null && feedbackMarkDebugCounters.IsValid)
                {
                    using var mapped = feedbackMarkDebugCounters.MapRange<uint>(
                        dstOffsetBytes: 0,
                        elementCount: FeedbackMarkDebugCounterCount,
                        access: MapBufferAccessMask.MapReadBit);

                    if (mapped.IsMapped && mapped.Span.Length >= FeedbackMarkDebugCounterCount)
                    {
                        lastMarkRejectPatchId0 = mapped.Span[0];
                        lastMarkRejectChunkSlotOob = mapped.Span[1];
                        lastMarkRejectGenMismatch = mapped.Span[2];
                    }
                }
            }

            if (config.Debug.LumOnRuntimeSelfCheckEnabled && toProcess > 0)
            {
                // Debug-only: estimate request diversity for the processed batch.
                // This helps diagnose "buffer saturated by one giant patch" starvation.
                var counts = new System.Collections.Generic.Dictionary<ulong, int>();
                uint topSlot = 0u;
                int topVpage = 0;
                int topCount = 0;

                for (int i = 0; i < toProcess; i++)
                {
                    uint slot = scratch[i].ChunkSlot;
                    int vpage = (int)scratch[i].VirtualPageIndex;
                    if ((uint)vpage >= (uint)VirtualPagesPerChunk)
                    {
                        continue;
                    }

                    int c = 1;
                    ulong key = LumonSceneVirtualPageKeyUtil.Pack(slot, (uint)vpage);
                    if (counts.TryGetValue(key, out int existing))
                    {
                        c = existing + 1;
                    }
                    counts[key] = c;

                    if (c > topCount)
                    {
                        topCount = c;
                        topSlot = slot;
                        topVpage = vpage;
                    }
                }

                lastUniqueVirtualPages = counts.Count;
                lastTopChunkSlot = topSlot;
                lastTopVirtualPage = topVpage;
                lastTopVirtualPageCount = topCount;

                int chunkSlotCount = Math.Max(1, nearGpu.PageTable.ChunkSlotCount);

                if (lastTopChunkSlots.Length != 4 || lastTopChunkSlotCounts.Length != 4)
                {
                    lastTopChunkSlots = new uint[4];
                    lastTopChunkSlotCounts = new int[4];
                }

                int[] slotCounts = ArrayPool<int>.Shared.Rent(chunkSlotCount);
                try
                {
                    Array.Clear(slotCounts, 0, chunkSlotCount);

                    for (int i = 0; i < toProcess; i++)
                    {
                        uint s = scratch[i].ChunkSlot;
                        if (s >= (uint)chunkSlotCount)
                        {
                            continue;
                        }

                        slotCounts[(int)s]++;
                    }

                    Span<int> topCounts = stackalloc int[4] { 0, 0, 0, 0 };
                    Span<uint> topSlots = stackalloc uint[4] { 0u, 0u, 0u, 0u };

                    for (int s = 0; s < chunkSlotCount; s++)
                    {
                        int c = slotCounts[s];
                        if (c <= topCounts[3])
                        {
                            continue;
                        }

                        // Insert into descending top-4.
                        int j = 3;
                        while (j > 0 && c > topCounts[j - 1])
                        {
                            topCounts[j] = topCounts[j - 1];
                            topSlots[j] = topSlots[j - 1];
                            j--;
                        }
                        topCounts[j] = c;
                        topSlots[j] = (uint)s;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        lastTopChunkSlots[i] = topSlots[i];
                        lastTopChunkSlotCounts[i] = topCounts[i];
                    }
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(slotCounts, clearArray: false);
                }
            }

            // Phase 9.1: allocate per-slot stats buffers for cooldown/backoff attribution.
            int slotCountForStats = slotOwners.Length;
            if (slotCountForStats > 0)
            {
                slotNewAllocsArr = ArrayPool<ushort>.Shared.Rent(slotCountForStats);
                slotStarvedBudgetArr = ArrayPool<ushort>.Shared.Rent(slotCountForStats);
                slotStarvedChunkBudgetArr = ArrayPool<ushort>.Shared.Rent(slotCountForStats);
                slotAllocFailuresArr = ArrayPool<ushort>.Shared.Rent(slotCountForStats);

                slotNewAllocs = slotNewAllocsArr.AsSpan(0, slotCountForStats);
                slotStarvedBudget = slotStarvedBudgetArr.AsSpan(0, slotCountForStats);
                slotStarvedChunkBudget = slotStarvedChunkBudgetArr.AsSpan(0, slotCountForStats);
                slotAllocFailures = slotAllocFailuresArr.AsSpan(0, slotCountForStats);
            }

            var cfg = config.LumOn.LumonScene;

            cpuProcessor!.Process(
                requests: scratch.AsSpan(0, toProcess),
                maxRequestsToProcess: maxRequestsToProcess,
                maxNewAllocations: maxNewAllocations,
                maxResidentPagesPerChunkSlot: ComputeMaxResidentPagesPerChunkSlot(physicalPools.Near.Plan.CapacityPages, in cfg),
                recaptureVirtualPageKeys: recaptureVirtualPageKeys is null ? ReadOnlySpan<ulong>.Empty : recaptureVirtualPageKeys.AsSpan(0, recaptureCount),
                recaptureCursor: ref recaptureCursor,
                maxRecapture: maxRecapture,
                captureWorkOut: captureScratch,
                relightWorkOut: relightScratch,
                captureCount: out int captureCount,
                relightCount: out int relightCount,
                stats: out lastProcessStats,
                newAllocationsByChunkSlot: slotNewAllocs,
                skippedGlobalBudgetByChunkSlot: slotStarvedBudget,
                skippedChunkBudgetByChunkSlot: slotStarvedChunkBudget,
                allocationFailuresByChunkSlot: slotAllocFailures);

            // The existing compactor reports unmapped visible demand only. Retain that recent signal
            // after allocation, without adding another feedback pass or claiming resident visibility.
            foreach (ulong key in visibleCapturePages.Keys.ToArray())
                if (!virtualToPhysical.ContainsKey(key)) visibleCapturePages.Remove(key);
            for (int i = 0; i < toProcess; i++)
            {
                ulong key = LumonSceneVirtualPageKeyUtil.Pack(scratch[i].ChunkSlot, scratch[i].VirtualPageIndex);
                if (virtualToPhysical.ContainsKey(key)) visibleCapturePages[key] = lastNearPriorityContext.NowTick;
            }

            // Phase 9.1: cooldown/backoff based on allocation failures and budget starvation.
            if (hasLastNearContexts && slotOwners.Length > 0)
            {
                ApplyNearCooldownFromSlotStats(
                    nowTick: lastNearStateContext.NowTick,
                    slotNewAllocs,
                    slotStarvedBudget,
                    slotStarvedChunkBudget,
                    slotAllocFailures);
            }

            lastCaptureCount = captureCount;
            lastRelightCount = relightCount;

            if (recaptureVirtualPageKeys is not null && recaptureCursor >= recaptureCount)
            {
                ArrayPool<ulong>.Shared.Return(recaptureVirtualPageKeys, clearArray: false);
                recaptureVirtualPageKeys = null;
                recaptureCount = 0;
                recaptureCursor = 0;
                captureSweepIdentities.Clear();
            }

            // v1: CPU-produced work overwrites the queues each frame.
            nearGpu.CaptureWork.ResetAndUpload(captureScratch.AsSpan(0, captureCount));
            for (int i = 0; i < captureCount; i++)
            {
                var item = captureScratch[i];
                diagnosticCaptureQueue.Observe(item.PhysicalPageId,
                    LumonSceneVirtualPageKeyUtil.Pack(item.ChunkSlot, item.VirtualPageIndex),
                    slotGenerations[item.ChunkSlot], Environment.TickCount64);
            }
            nearGpu.RelightWork.ResetAndUpload(relightScratch.AsSpan(0, relightCount));

            // Stash vpages captured in the SSBO for later flag finalization this frame.
            // We keep a copy on CPU via pageTableMirror; flag updates are based on the capture work contents.
            return captureCount;
        }
        finally
        {
            ArrayPool<LumonScenePageRequestGpu>.Shared.Return(scratch, clearArray: false);
            ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Return(captureScratch, clearArray: false);
            ArrayPool<LumonSceneRelightWorkGpu>.Shared.Return(relightScratch, clearArray: false);

            if (slotNewAllocsArr is not null) ArrayPool<ushort>.Shared.Return(slotNewAllocsArr, clearArray: true);
            if (slotStarvedBudgetArr is not null) ArrayPool<ushort>.Shared.Return(slotStarvedBudgetArr, clearArray: true);
            if (slotStarvedChunkBudgetArr is not null) ArrayPool<ushort>.Shared.Return(slotStarvedChunkBudgetArr, clearArray: true);
            if (slotAllocFailuresArr is not null) ArrayPool<ushort>.Shared.Return(slotAllocFailuresArr, clearArray: true);
        }
    }

    private void ApplyNearCooldownFromSlotStats(
        long nowTick,
        ReadOnlySpan<ushort> slotNewAllocs,
        ReadOnlySpan<ushort> slotStarvedBudget,
        ReadOnlySpan<ushort> slotStarvedChunkBudget,
        ReadOnlySpan<ushort> slotAllocFailures)
    {
        int slotCount = slotOwners.Length;
        if (slotCount <= 0)
        {
            return;
        }

        int n = Math.Min(slotCount, slotNewAllocs.Length);
        n = Math.Min(n, slotStarvedBudget.Length);
        n = Math.Min(n, slotStarvedChunkBudget.Length);
        n = Math.Min(n, slotAllocFailures.Length);
        if (n <= 0)
        {
            return;
        }

        for (int slot = 0; slot < n; slot++)
        {
            ushort newAllocs = slotNewAllocs[slot];
            ushort starvedBudget = slotStarvedBudget[slot];
            ushort starvedChunkBudget = slotStarvedChunkBudget[slot];
            ushort allocFailures = slotAllocFailures[slot];

            if ((newAllocs | starvedBudget | starvedChunkBudget | allocFailures) == 0)
            {
                continue;
            }

            VectorInt3 owner = slotOwners[slot];
            var coord = new LumonSceneChunkCoord(owner.X, owner.Y, owner.Z);
            WorldCellKey key = WorldCellKey.FromLumonSceneNear(coord.ToKey());

            if (!nearRegionScheduler.TryGetCell(key, out LumonSceneRegionCell cell))
            {
                continue;
            }

            // Backoff precedence: allocation failure > global budget > success reset.
            if (allocFailures > 0)
            {
                cell.ApplyAllocationFailureBackoff(nowTick);
            }
            else if (starvedChunkBudget > 0)
            {
                cell.ApplyBudgetStarvationBackoff(nowTick, isPerSlotBudget: true);
            }
            else if (starvedBudget > 0)
            {
                cell.ApplyBudgetStarvationBackoff(nowTick, isPerSlotBudget: false);
            }
            else if (newAllocs > 0)
            {
                cell.NotifyAllocationSuccess(nowTick);
            }

            // Refresh queues after cooldown update.
            cell.EnqueueStateAndWork(nearRegionScheduler, in lastNearPriorityContext);
        }
    }

    /// <summary>Admits an explicit invalidation sweep or resumes failed captures after the previous sweep drains.</summary>
    private void EnsureRecaptureListIfRequested()
    {
        if (Interlocked.Exchange(ref recaptureAllRequested, 0) == 0)
        {
            ResumeCaptureRetries();
            return;
        }

        // Coalesce explicit demand without restarting an in-progress finite sweep.
        // Only active cells receive new forced demand; already deferred resident tickets retain ownership.
        bool canFilter = hasLastNearContexts && slotOwners.Length > 0;

        foreach (ulong key in virtualToPhysical.Keys)
        {
            if (!canFilter)
            {
                QueueCaptureRetry(virtualToPhysical[key], key);
                continue;
            }

            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
            if (slot >= (uint)slotOwners.Length)
            {
                continue;
            }

            VectorInt3 owner = slotOwners[slot];
            var coord = new LumonSceneChunkCoord(owner.X, owner.Y, owner.Z);
            WorldCellKey cellKey = WorldCellKey.FromLumonSceneNear(coord.ToKey());

            if (nearRegionScheduler.TryGetCell(cellKey, out LumonSceneRegionCell cell)
                && cell.DesiredState == WorldCellDesiredState.Active)
            {
                QueueCaptureRetry(virtualToPhysical[key], key);
            }
        }

        ResumeCaptureRetries();
    }

    private void ResetRecaptureList()
    {
        if (recaptureVirtualPageKeys is not null)
        {
            ArrayPool<ulong>.Shared.Return(recaptureVirtualPageKeys, clearArray: false);
        }

        recaptureVirtualPageKeys = null;
        recaptureCount = 0;
        recaptureCursor = 0;
        captureSweepIdentities.Clear();
    }

    /// <summary>Submits capture only with all resources present; unsubmitted queue items cannot establish capture validity.</summary>
    private bool DispatchVoxelCapture(int captureCount)
    {
        if (captureCount <= 0)
        {
            return false;
        }

        if (!EnsureCaptureVoxelShader())
        {
            return false;
        }

        var atlases = physicalPools.Near.GpuResources;
        if (atlases is null)
        {
            return false;
        }

        if (slotInfoBuffer is null)
        {
            return false;
        }

        int tileSize = physicalPools.Near.Plan.TileSizeTexels;
        int tilesPerAxis = physicalPools.Near.Plan.TilesPerAxis;
        int tilesPerAtlas = physicalPools.Near.Plan.TilesPerAtlas;

        using (captureVoxelShader!.UseScope())
        {
            using var gpuScope = GlGpuProfiler.Instance.Scope("Capture.Voxel");

            captureVoxelShader.BindCaptureWorkSsbo(nearGpu.CaptureWork.Items);
            captureVoxelShader.BindPatchMetaSsbo(nearGpu.PatchMetadata.Ssbo);
            captureVoxelShader.BindChunkSlotInfoSsbo(slotInfoBuffer.Ssbo);

            captureVoxelShader.BindDepthAtlasImage(atlases.DepthAtlas, access: TextureAccess.WriteOnly);
            captureVoxelShader.BindMaterialAtlasImage(atlases.MaterialAtlas, access: TextureAccess.WriteOnly);

            captureVoxelShader.BindSharedGeometry(traceGeometry?.PrepareScene());
            captureVoxelShader.SetAtlasLayout(
                tileSizeTexels: (uint)tileSize,
                tilesPerAxis: (uint)tilesPerAxis,
                tilesPerAtlas: (uint)tilesPerAtlas,
                borderTexels: 0u);



            int gx = (tileSize + 7) / 8;
            int gy = (tileSize + 7) / 8;
            captureVoxelShader.Diagnostics.Enabled = config.LumOn.LumonScene.SurfaceWorkDiagnosticsEnabled;
            captureVoxelShader.Dispatch(gx, gy, captureCount);
        }

        // Capture writes:
        // - images: depth/material atlases (imageStore)
        // - SSBO: patch metadata (std430)
        // These are consumed later in the frame by:
        // - relight compute (texture fetch + SSBO reads)
        // - debug/material sampling passes (texture fetch)
        GL.MemoryBarrier(
            MemoryBarrierFlags.ShaderImageAccessBarrierBit
            | MemoryBarrierFlags.ShaderStorageBarrierBit
            | MemoryBarrierFlags.BufferUpdateBarrierBit
            | MemoryBarrierFlags.TextureFetchBarrierBit);
        return true;
    }

    /// <summary>Publishes successful captures and retains unresolved pages for bounded later retries.</summary>
    private void FinalizeCaptureFlagsFromGpuQueue(int captureCount)
    {
        if (captureCount <= 0)
        {
            return;
        }

        // Read back the capture work items (small, bounded) so we can clear NeedsCapture flags in the page table.
        LumonSceneCaptureWorkGpu[] items = ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Rent(captureCount);
        diagnosticCaptureAttempts += captureCount;
        try
        {
            long mapStart = System.Diagnostics.Stopwatch.GetTimestamp();
            using var mapped = nearGpu.CaptureWork.Items.MapRange<LumonSceneCaptureWorkGpu>(0, captureCount, MapBufferAccessMask.MapReadBit);
            diagnosticCaptureMapMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(mapStart).TotalMilliseconds;
            diagnosticCaptureMapCalls++;
            if (!mapped.IsMapped)
            {
                diagnosticCaptureReadFailures++;
                captureRetryVersion++;
                return;
            }

            mapped.Span.CopyTo(items.AsSpan(0, captureCount));
            var captureScene = traceGeometry?.PrepareScene();

            for (int i = 0; i < captureCount; i++)
            {
                // The shader marks unavailable material/geometry with the high bit; retry the page.
                bool shaderFailed = (items[i].VirtualPageIndex & 0x80000000u) != 0;
                bool identityFailed = !shaderFailed && !RecordCaptureIdentity(items[i], captureScene);
                if (identityFailed) diagnosticCaptureIdentityFailures++;
                if (shaderFailed || identityFailed)
                {
                    diagnosticCaptureFailures++;
                    captureAdmission.Reject(items[i].PhysicalPageId);
                    continue;
                }
                uint chunkSlot = items[i].ChunkSlot;
                int vpage = (int)items[i].VirtualPageIndex;
                if ((uint)vpage >= (uint)VirtualPagesPerChunk)
                {
                    continue;
                }

                int idx = checked((int)chunkSlot * VirtualPagesPerChunk + vpage);
                if ((uint)idx >= (uint)pageTableMirror.Length)
                {
                    continue;
                }

                var entry = pageTableMirror[idx];
                uint pid = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry);
                if (pid == 0)
                {
                    continue;
                }

                var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
                flags &= ~LumonScenePageTableEntryPacking.Flags.NeedsCapture;

                LumonScenePageTableEntry updated = LumonScenePageTableEntryPacking.Pack(pid, flags);
                pageTableMirror[idx] = updated;
                pageTableStats.ApplyEntryChange(chunkSlot, in entry, in updated);
                UploadPageTableEntryMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: vpage, updated.Packed);
                diagnosticCaptureQueue.Complete(items[i].PhysicalPageId, Environment.TickCount64);
                if (captureRetries.Remove(LumonSceneVirtualPageKeyUtil.Pack(chunkSlot, (uint)vpage))) captureRetryVersion++;
            }
        }
        finally
        {
            ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Return(items, clearArray: false);
        }
    }

    private unsafe void UploadPageTableEntryMip0(int chunkSlot, int virtualPageIndex, uint packedEntry)
    {
        int x = virtualPageIndex % VirtualPageTableW;
        int y = virtualPageIndex / VirtualPageTableW;

        int texId = nearGpu.PageTable.PageTableMip0.TextureId;
        using var _ = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, unit: 0, texId);

        uint value = packedEntry;
        GL.TexSubImage3D(
            TextureTarget.Texture2DArray,
            level: 0,
            xoffset: x,
            yoffset: y,
            zoffset: chunkSlot,
            width: 1,
            height: 1,
            depth: 1,
            format: PixelFormat.RedInteger,
            type: PixelType.UnsignedInt,
            pixels: (IntPtr)(&value));
    }

    private sealed class RendererPageTableWriter : ILumonScenePageTableWriter
    {
        private readonly LumonSceneFeedbackUpdateRenderer owner;

        public RendererPageTableWriter(LumonSceneFeedbackUpdateRenderer owner) => this.owner = owner;

        public void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry)
            => owner.UploadPageTableEntryMip0(chunkSlot, virtualPageIndex, packedEntry);
    }

    internal bool TryGetSelfCheckLine(out string line)
    {
        line = string.Empty;

        if (!configured || physicalPools.Near.GpuResources is null)
        {
            return false;
        }

        int residentPages = virtualToPhysical.Count;
        int cap = physicalPools.Near.PagePool.CapacityPages;

        LumonSceneChunkSlotPageTableStats totals = pageTableStats.Total;
        int ready = totals.ReadyToSample;
        int needsCap = totals.NeedsCapture;
        int needsRel = totals.NeedsRelight;

        string topSlots = string.Empty;
        if (lastTopChunkSlots.Length == 4 && lastTopChunkSlotCounts.Length == 4 && lastTopChunkSlotCounts[0] > 0)
        {
            topSlots =
                $" slots:{lastTopChunkSlots[0]}={lastTopChunkSlotCounts[0]}" +
                $" ({lastTopChunkSlots[1]}={lastTopChunkSlotCounts[1]} {lastTopChunkSlots[2]}={lastTopChunkSlotCounts[2]} {lastTopChunkSlots[3]}={lastTopChunkSlotCounts[3]})";
        }

        line =
            $"LS: req:{lastRequestCount} read:{lastRequestsRead} dropG:{lastRequestsDroppedGpu} dropC:{lastRequestsDroppedCpuBudget} dropI:{lastRequestsDroppedInactive} proc:{lastProcessStats.RequestsConsidered} " +
            $"exist:{lastProcessStats.RequestsAcceptedExisting} new:{lastProcessStats.RequestsAllocatedNew} ev:{lastProcessStats.AllocationEvictions} fail:{lastProcessStats.AllocationFailures} " +
            $"skipBud:{lastProcessStats.RequestsSkippedBudget} skipChunk:{lastProcessStats.RequestsSkippedChunkBudget} genMis:{lastMarkRejectGenMismatch} slotOob:{lastMarkRejectChunkSlotOob} pid0:{lastMarkRejectPatchId0} " +
            $"uniq:{lastUniqueVirtualPages} top:{lastTopChunkSlot},{lastTopVirtualPage}:{lastTopVirtualPageCount}{topSlots} " +
            $"prioK:{lastPrioritizeTopK} prioReq:{lastRequestsPrioritized} " +
            $"cells:{lastRegionCells} dA:{lastRegionDesiredActive} dL:{lastRegionDesiredLoaded} dU:{lastRegionDesiredUnloaded} supp:{lastRegionSuppressed} " +
            $"act:{lastRegionActive} load:{lastRegionLoaded} winL:{lastLoadedWindowSize} winA:{lastActiveWindowSize} " +
            $"budReq:{lastBudgetMaxRequests} budNew:{lastBudgetMaxNewAllocs} budRel:{lastBudgetMaxRelightPages} " +
            $"qC:{nearRegionScheduler.CaptureCount} qR:{nearRegionScheduler.RelightCount} " +
            $"wOff:{lastWorldChunkCoordOffset.X},{lastWorldChunkCoordOffset.Y},{lastWorldChunkCoordOffset.Z} " +
            $"wRem:{lastWorldBlockOffsetRem.X:0.##},{lastWorldBlockOffsetRem.Y:0.##},{lastWorldBlockOffsetRem.Z:0.##} " +
            $"res:{residentPages}/{cap} ready:{ready} nc:{needsCap} nr:{needsRel} capQ:{lastCaptureCount} relQ:{lastRelightCount} " +
            $"rc:{lastProcessStats.RecaptureSucceeded}/{lastProcessStats.RecaptureAttempted} " +
            $"captureFail:{diagnosticCaptureFailures}/{diagnosticCaptureAttempts} captureReadFail:{diagnosticCaptureReadFailures} " +
            $"capturePending:{captureRetries.Count} captureEligibilityChecks:{captureAdmission.Checks} captureDeferredChecks:{captureAdmission.Deferred}";

        return true;
    }

    private static int ComputeMaxResidentPagesPerChunkSlot(int physicalCapacityPages, in VgeConfig.LumOnSettingsConfig.LumonSceneConfig cfg)
    {
        int requestedChunks = LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesBoxField(cfg.NearRadiusChunks, cfg.NearRadiusYChunks);
        int requestedPerChunk = Math.Max(64, cfg.NearPagesPerChunkBudget);
        physicalCapacityPages = Math.Max(1, physicalCapacityPages);

        // Fairness cap: do not allow a chunk slot to exceed the pool's effective average pages-per-chunk
        // (based on the requestedChunks "covered + extra" sizing invariant). This prevents permanent starvation
        // and preserves headroom for re-anchors without requiring eviction while loaded.
        if (requestedChunks <= 0)
        {
            return requestedPerChunk;
        }

        int effectiveAvg = Math.Max(1, physicalCapacityPages / requestedChunks);
        return Math.Min(requestedPerChunk, effectiveAvg);
    }
}
