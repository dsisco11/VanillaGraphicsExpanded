using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.5: Trace scene v1 - occupancy clipmap (region-driven, GPU-built).
/// Stores packed R32UI payload per cell (block/sun/light/material indirection), written into the clipmap via GL 4.3 compute.
/// </summary>
internal sealed class LumonSceneOccupancyClipmapUpdateRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 0.99975;
    private const int RenderRangeValue = 1;

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly IEventAPI commonEvents;

    private LumonSceneOccupancyClipmapGpuResources? resources;
    private LumonSceneTraceSceneClipmapGpuBuildDispatcher? gpuDispatcher;

    private int lastConfigHash;

    private readonly LumonSceneTraceSceneLightIdRegistry lightIds = new();
    private readonly float[] lightLutUpload = new float[LumonSceneOccupancyClipmapGpuResources.MaxLightColors * 4];
    private readonly LumonScenePbrSurfaceLutRegistry surfaceLut = new();
    private readonly uint[] surfaceLutUpload = new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4];
    private readonly LumonSceneTraceSceneMaterialPaletteRegistry materialPalette;
    private readonly uint[] materialPaletteUpload = new uint[LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries * 4];

    private LevelState[] levelStates = Array.Empty<LevelState>();

    private int rebuildAllRequested;

    private ChunkProcessingService? chunkProcessing;
    private LumonSceneTraceSceneChunkVersionProvider? chunkVersions;
    private LumonSceneTraceSceneChunkSnapshotSource? snapshotSource;
    private readonly LumonSceneTraceSceneRegionProcessor regionProcessor = new();

    private readonly TraceSceneRegionScheduler regionScheduler = new();
    private readonly Dictionary<ulong, InFlightRegion> inFlightByRegion = new();
    private readonly ConcurrentQueue<InFlightCompletion> completedRegions = new();

    private VectorInt3 currentWindowRegionMin;
    private VectorInt3 currentWindowRegionMax;
    private bool hasWindowRegionBounds;

    private long traceSceneNowMs;

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    public LumonSceneOccupancyClipmapGpuResources? Resources => resources;

    internal bool TryGetTraceSceneSchedulerTopKLine(int k, out string line)
    {
        if (k <= 0)
        {
            line = string.Empty;
            return false;
        }

        if (!config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled)
        {
            line = string.Empty;
            return false;
        }

        long nowMs = capi.World.ElapsedMilliseconds;
        return regionScheduler.TryGetTopK(k, nowMs, out line);
    }

    internal string DumpTraceSceneSchedulerState(int topN)
    {
        long nowMs = capi.World.ElapsedMilliseconds;
        return regionScheduler.DumpState(topN, nowMs);
    }

    internal static bool TryGetDispatchPayload(
        in ChunkWorkResult<LumonSceneTraceSceneRegionArtifact> result,
        out ReadOnlyMemory<uint> payloadWords)
    {
        if (result.Status == ChunkWorkStatus.Success && result.Artifact is not null)
        {
            payloadWords = result.Artifact.PayloadWords;
            return true;
        }

        payloadWords = default;
        return false;
    }

    internal bool TryGetLevel0RuntimeParams(out VectorInt3 originMinCell, out VectorInt3 ring, out int resolution)
    {
        originMinCell = default;
        ring = default;
        resolution = 0;

        if (resources is null || levelStates.Length <= 0)
        {
            return false;
        }

        var ls = levelStates[0];
        if (!ls.HasAnchor)
        {
            return false;
        }

        originMinCell = ls.OriginMinCell;
        ring = ls.Ring;
        resolution = ls.Resolution;
        return true;
    }

    private sealed class LevelState
    {
        public required int Level;
        public required int Resolution;
        public required int SpacingBlocks;

        public VectorInt3 AnchorCell;
        public VectorInt3 OriginMinCell;
        public VectorInt3 Ring;
        public bool HasAnchor;
    }

    private readonly record struct InFlightRegion(int Version, Task<ChunkWorkResult<LumonSceneTraceSceneRegionArtifact>> Task);

    private readonly record struct InFlightCompletion(
        ulong RegionKeyPacked,
        int RequestedVersion,
        Task<ChunkWorkResult<LumonSceneTraceSceneRegionArtifact>> Task);

    private readonly record struct CompletionEnqueueState(
        ConcurrentQueue<InFlightCompletion> Queue,
        ulong RegionKeyPacked,
        int RequestedVersion);

    public LumonSceneOccupancyClipmapUpdateRenderer(ICoreClientAPI capi, VgeConfig config)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        commonEvents = ((ICoreAPI)capi).Event;

        materialPalette = new LumonSceneTraceSceneMaterialPaletteRegistry(surfaceLut);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "vge_lumonscene_occupancy_clipmap");
        capi.Event.LeaveWorld += OnLeaveWorld;
        commonEvents.ChunkDirty += OnChunkDirty;
    }

    public void NotifyAllDirty(string reason)
    {
        _ = reason;
        Interlocked.Exchange(ref rebuildAllRequested, 1);
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

        if (!TryGetCameraPosBlock(out int camX, out int camY, out int camZ))
        {
            return;
        }

        EnsureResourcesAndConfig();
        if (resources is null)
        {
            return;
        }

        // Scheduler cooldown/backoff operates in milliseconds.
        traceSceneNowMs = capi.World.ElapsedMilliseconds;

        var traceCfg = config.LumOn.LumonScene.TraceScene;

        // Apply rebuild request.
        if (Interlocked.Exchange(ref rebuildAllRequested, 0) != 0)
        {
            RequestRebuildAll();
        }

        bool level0Moved = false;
        bool maxLevelMoved = false;

        // Update anchor + ring mapping for all levels.
        for (int i = 0; i < levelStates.Length; i++)
        {
            bool moved = UpdateAnchor(levelStates[i], camX, camY, camZ);
            if (i == 0)
            {
                level0Moved |= moved;
            }

            if (i == levelStates.Length - 1)
            {
                maxLevelMoved |= moved;
            }
        }

        // Update region-window bounds for the coarsest level (largest world coverage).
        // Seeding is incremental, so we avoid O(windowVolume) work per frame for large configs.
        if (levelStates.Length > 0 && levelStates[^1].HasAnchor)
        {
            ComputeWindowRegionBounds(levelStates[^1], out VectorInt3 regionMin, out VectorInt3 regionMax);
            UpdateWindowAndEnqueueNew(regionMin, regionMax, enqueueAll: !hasWindowRegionBounds || maxLevelMoved);
        }

        // Refresh priorities + incrementally seed the window.
        var priorityContext = new WorldCellPriorityContext(
            CameraBlockPos: new VectorInt3(camX, camY, camZ),
            AnchorBlockPos: levelStates.Length > 0 && levelStates[0].HasAnchor
                ? new VectorInt3(levelStates[0].AnchorCell.X, levelStates[0].AnchorCell.Y, levelStates[0].AnchorCell.Z)
                : new VectorInt3(camX, camY, camZ),
            HasAnchor: levelStates.Length > 0 && levelStates[0].HasAnchor,
            WindowMinRegion: default,
            WindowMaxRegion: default,
            HasWindow: false,
            NowTick: traceSceneNowMs);

        RefreshScheduler(
            in priorityContext,
            refreshBudgetMs: traceCfg.ClipmapRefreshBudgetMs,
            maxCells: Math.Max(256, traceCfg.ClipmapMaxInFlightRegions * 4));

        // Start async region extraction jobs under budget.
        IssueRegionRequests(
            issueBudgetMs: traceCfg.ClipmapIssueBudgetMs,
            maxInFlightRegions: traceCfg.ClipmapMaxInFlightRegions);

        // Consume completed results and dispatch region->clipmap compute writes under budgets.
        DispatchCompleted(
            dispatchBudgetMs: traceCfg.ClipmapDispatchBudgetMs,
            maxUploadsPerFrame: traceCfg.ClipmapMaxRegionUploadsPerFrame,
            maxRegionsPerFrame: traceCfg.ClipmapMaxRegionsDispatchedPerFrame);

        LumonSceneTraceSceneMetrics.SetState(
            queueHighLength: regionScheduler.EligibleNearCount,
            queueLowLength: regionScheduler.EligibleFarCount,
            inFlight: inFlightByRegion.Count,
            appliedRegions: regionScheduler.AppliedCount,
            suppressed: regionScheduler.SuppressedCount);

        if (lightIds.TryCopyAndClearDirtyLut(lightLutUpload))
        {
            resources.LightColorLut.UploadDataImmediate(lightLutUpload);
        }

        // Opportunistically upgrade any surface LUT entries that were created before PBR surfaces were available.
        _ = surfaceLut.UpgradeUnresolvedEntries(maxToUpgrade: 256);

        if (surfaceLut.TryCopyAndClearDirtyLut(surfaceLutUpload))
        {
            resources.SurfaceLut.UploadDataImmediate(surfaceLutUpload);
        }

        if (materialPalette.TryCopyAndClearDirtyPalette(materialPaletteUpload))
        {
            resources.MaterialPalette.UploadDataImmediate(materialPaletteUpload);
        }
    }

    public void Dispose()
    {
        capi.Event.LeaveWorld -= OnLeaveWorld;

        commonEvents.ChunkDirty -= OnChunkDirty;

        chunkProcessing?.Dispose();
        chunkProcessing = null;
        chunkVersions = null;
        snapshotSource = null;

        gpuDispatcher?.Dispose();
        gpuDispatcher = null;

        resources?.Dispose();
        resources = null;
    }

    private void OnLeaveWorld()
    {
        lastConfigHash = 0;

        chunkProcessing?.Dispose();
        chunkProcessing = null;
        chunkVersions = null;
        snapshotSource = null;

        gpuDispatcher?.Dispose();
        gpuDispatcher = null;

        resources?.Dispose();
        resources = null;

        levelStates = Array.Empty<LevelState>();

        lightIds.Reset();
        surfaceLut.Reset();
        materialPalette.Reset();

        regionScheduler.Reset();
        inFlightByRegion.Clear();
        ClearCompletedRegionsQueue();
        hasWindowRegionBounds = false;
        traceSceneNowMs = 0;

        rebuildAllRequested = 0;
    }

    private bool TryGetCameraPosBlock(out int x, out int y, out int z)
    {
        var player = capi.World?.Player;
        if (player?.Entity is null)
        {
            x = y = z = 0;
            return false;
        }

        double px = player.Entity.CameraPos.X;
        double py = player.Entity.CameraPos.Y;
        double pz = player.Entity.CameraPos.Z;
        x = (int)Math.Floor(px);
        y = (int)Math.Floor(py);
        z = (int)Math.Floor(pz);
        return true;
    }

    private void EnsureResourcesAndConfig()
    {
        var traceCfg = config.LumOn.LumonScene.TraceScene;
        int resolution = traceCfg.ClipmapResolution;
        int levels = traceCfg.ClipmapLevels;

        int configHash = HashCode.Combine(resolution, levels);
        if (resources is not null && configHash == lastConfigHash)
        {
            return;
        }

        lastConfigHash = configHash;

        resources?.Dispose();
        resources = null;

        resolution = Math.Clamp(resolution, 8, 256);
        levels = Math.Clamp(levels, 1, 8);

        resources = new LumonSceneOccupancyClipmapGpuResources(
            resolution: resolution,
            levels: levels,
            debugNamePrefix: "LumOn.LumonScene.TraceScene");

        levelStates = new LevelState[levels];
        for (int i = 0; i < levels; i++)
        {
            levelStates[i] = new LevelState
            {
                Level = i,
                Resolution = resolution,
                SpacingBlocks = 1 << i
            };
        }

        // Reset trace-scene state (new resources => stale GPU contents).
        lightIds.Reset();
        materialPalette.Reset();

        regionScheduler.Reset();
        inFlightByRegion.Clear();
        ClearCompletedRegionsQueue();
        hasWindowRegionBounds = false;

        chunkProcessing?.Dispose();
        chunkProcessing = null;
        chunkVersions = new LumonSceneTraceSceneChunkVersionProvider();
        snapshotSource = new LumonSceneTraceSceneChunkSnapshotSource(capi, chunkVersions, lightIds, materialPalette);
        chunkProcessing = new ChunkProcessingService(snapshotSource, chunkVersions);

        gpuDispatcher ??= new LumonSceneTraceSceneClipmapGpuBuildDispatcher(capi);
    }

    private void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        if (chunkVersions is null)
        {
            return;
        }

        ChunkKey key = ChunkKey.FromChunkCoords(chunkCoord.X, chunkCoord.Y, chunkCoord.Z);
        chunkVersions.MarkDirty(key);

        int currentVersion = chunkVersions.GetCurrentVersion(key);
        regionScheduler.NotifyChunkDirty(key, currentVersion: currentVersion, nowTick: traceSceneNowMs, reason: reason.ToString());

        // Loadedness hint: ChunkDirty implies the chunk was observed loaded.
        regionScheduler.NotifyChunkSeenLoaded(key, nowTick: traceSceneNowMs);
    }

    private void RequestRebuildAll()
    {
        chunkVersions?.BumpGlobalGeneration();

        regionScheduler.Reset();
        inFlightByRegion.Clear();
        ClearCompletedRegionsQueue();
        hasWindowRegionBounds = false;
        traceSceneNowMs = 0;
    }

    private void ClearCompletedRegionsQueue()
    {
        while (completedRegions.TryDequeue(out _))
        {
        }
    }

    private bool UpdateAnchor(LevelState ls, int camX, int camY, int camZ)
    {
        int level = ls.Level;
        int res = ls.Resolution;
        int half = res / 2;

        VectorInt3 newAnchorCell = new(camX >> level, camY >> level, camZ >> level);

        if (!ls.HasAnchor)
        {
            ls.AnchorCell = newAnchorCell;
            ls.OriginMinCell = new VectorInt3(newAnchorCell.X - half, newAnchorCell.Y - half, newAnchorCell.Z - half);
            ls.Ring = VectorInt3.Zero;
            ls.HasAnchor = true;
            return true;
        }

        int deltaX = newAnchorCell.X - ls.AnchorCell.X;
        int deltaY = newAnchorCell.Y - ls.AnchorCell.Y;
        int deltaZ = newAnchorCell.Z - ls.AnchorCell.Z;
        if (deltaX == 0 && deltaY == 0 && deltaZ == 0)
        {
            return false;
        }

        ls.AnchorCell = newAnchorCell;
        ls.OriginMinCell = new VectorInt3(newAnchorCell.X - half, newAnchorCell.Y - half, newAnchorCell.Z - half);

        ls.Ring = new VectorInt3(
            Wrap(ls.Ring.X + deltaX, res),
            Wrap(ls.Ring.Y + deltaY, res),
            Wrap(ls.Ring.Z + deltaZ, res));

        return true;
    }

    private static void ComputeWindowRegionBounds(LevelState level0, out VectorInt3 regionMin, out VectorInt3 regionMax)
    {
        int res = level0.Resolution;

        LumonSceneTraceSceneClipmapMath.ComputeWorldCellBoundsForLevelWindow(
            originMinCellLevel: level0.OriginMinCell,
            level: level0.Level,
            resolution: res,
            worldMinCell: out VectorInt3 worldMinCell,
            worldMaxInclusiveCell: out VectorInt3 worldMaxInclusiveCell);

        regionMin = new VectorInt3(worldMinCell.X >> 5, worldMinCell.Y >> 5, worldMinCell.Z >> 5);
        regionMax = new VectorInt3(worldMaxInclusiveCell.X >> 5, worldMaxInclusiveCell.Y >> 5, worldMaxInclusiveCell.Z >> 5);
    }

    private void UpdateWindowAndEnqueueNew(in VectorInt3 newMin, in VectorInt3 newMax, bool enqueueAll)
    {
        VectorInt3 prevMin = currentWindowRegionMin;
        VectorInt3 prevMax = currentWindowRegionMax;
        bool hadPrev = hasWindowRegionBounds;

        if (hadPrev && newMin == prevMin && newMax == prevMax)
        {
            return;
        }

        currentWindowRegionMin = newMin;
        currentWindowRegionMax = newMax;
        hasWindowRegionBounds = true;

        // Window changes must drop any in-flight work outside the new window so it won't be dispatched.
        if (hadPrev && (prevMin != currentWindowRegionMin || prevMax != currentWindowRegionMax))
        {
            TrimInFlightToWindow();
        }

        regionScheduler.SetWindow(currentWindowRegionMin, currentWindowRegionMax);

        _ = enqueueAll;
    }

    private void TrimInFlightToWindow()
    {
        if (!hasWindowRegionBounds)
        {
            return;
        }

        // Drop anything outside window so it won't be dispatched (work still completes in background).
        if (inFlightByRegion.Count > 0)
        {
            ulong[] keys = ArrayPool<ulong>.Shared.Rent(inFlightByRegion.Count);
            int keyCount = 0;
            try
            {
                foreach (var kvp in inFlightByRegion)
                {
                    keys[keyCount++] = kvp.Key;
                }

                for (int i = 0; i < keyCount; i++)
                {
                    ChunkKey ck = new ChunkKey(keys[i]);
                    ck.Decode(out int rx, out int ry, out int rz);
                    if (!IsInWindow(new VectorInt3(rx, ry, rz)))
                    {
                        if (inFlightByRegion.Remove(keys[i], out InFlightRegion inflight))
                        {
                            regionScheduler.OnRequestCompleted(ck, ChunkWorkStatus.Superseded, requestedVersion: inflight.Version, nowTick: traceSceneNowMs);
                            LumonSceneTraceSceneMetrics.OnRegionCompleted(ChunkWorkStatus.Superseded);
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(keys, clearArray: false);
            }
        }
    }

    private bool IsInWindow(in VectorInt3 regionCoord)
    {
        return regionCoord.X >= currentWindowRegionMin.X && regionCoord.X <= currentWindowRegionMax.X
            && regionCoord.Y >= currentWindowRegionMin.Y && regionCoord.Y <= currentWindowRegionMax.Y
            && regionCoord.Z >= currentWindowRegionMin.Z && regionCoord.Z <= currentWindowRegionMax.Z;
    }


    private void RefreshScheduler(in WorldCellPriorityContext context, float refreshBudgetMs, int maxCells)
    {
        if (refreshBudgetMs <= 0f || maxCells <= 0)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        long budgetTicks = (long)(refreshBudgetMs * 0.001f * Stopwatch.Frequency);
        if (budgetTicks <= 0)
        {
            return;
        }

        int refreshed = 0;
        while (refreshed < maxCells)
        {
            if (Stopwatch.GetTimestamp() - start > budgetTicks)
            {
                break;
            }

            int step = Math.Min(128, maxCells - refreshed);
            int did = regionScheduler.RefreshPriorities(in context, step);
            refreshed += did;

            if (did < step)
            {
                break;
            }
        }
    }

    private void IssueRegionRequests(float issueBudgetMs, int maxInFlightRegions)
    {
        if (chunkProcessing is null || chunkVersions is null || issueBudgetMs <= 0f || maxInFlightRegions <= 0)
        {
            return;
        }

        int inFlightRoom = maxInFlightRegions - inFlightByRegion.Count;
        if (inFlightRoom <= 0)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        long budgetTicks = (long)(issueBudgetMs * 0.001f * Stopwatch.Frequency);
        if (budgetTicks <= 0)
        {
            return;
        }

        int issued = 0;
        while (issued < inFlightRoom)
        {
            if (Stopwatch.GetTimestamp() - start > budgetTicks)
            {
                break;
            }

            if (!regionScheduler.TryDequeueNextEligible(traceSceneNowMs, out ChunkKey key, out int priorityHint))
            {
                break;
            }

            int version = chunkVersions.GetCurrentVersion(key);

            if (inFlightByRegion.TryGetValue(key.Packed, out InFlightRegion existing)
                && existing.Version == version
                && !existing.Task.IsCompleted)
            {
                // Keep scheduler in sync if we somehow lost in-flight bookkeeping.
                regionScheduler.OnRequestIssued(key, version, nowTick: traceSceneNowMs);
                continue;
            }

            regionScheduler.OnRequestIssued(key, version, nowTick: traceSceneNowMs);

            var options = new ChunkWorkOptions { Priority = priorityHint };
            Task<ChunkWorkResult<LumonSceneTraceSceneRegionArtifact>> task =
                chunkProcessing.RequestAsync(key, version, regionProcessor, options);

            inFlightByRegion[key.Packed] = new InFlightRegion(version, task);

            if (task.IsCompleted)
            {
                completedRegions.Enqueue(new InFlightCompletion(key.Packed, version, task));
            }
            else
            {
                _ = task.ContinueWith(
                    static (t, stateObj) =>
                    {
                        var state = (CompletionEnqueueState)stateObj!;
                        state.Queue.Enqueue(new InFlightCompletion(state.RegionKeyPacked, state.RequestedVersion, t));
                    },
                    state: new CompletionEnqueueState(completedRegions, key.Packed, version),
                    cancellationToken: CancellationToken.None,
                    continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
                    scheduler: TaskScheduler.Default);
            }

            LumonSceneTraceSceneMetrics.OnRegionRequestsIssued(1);
            issued++;
        }
    }

    private void DispatchCompleted(float dispatchBudgetMs, int maxUploadsPerFrame, int maxRegionsPerFrame)
    {
        if (resources is null || gpuDispatcher is null || dispatchBudgetMs <= 0f)
        {
            return;
        }

        using var cpuScope = Profiler.BeginScope("LumOn.TraceScene.DispatchCompleted", "Render");

        long start = Stopwatch.GetTimestamp();
        long budgetTicks = (long)(dispatchBudgetMs * 0.001f * Stopwatch.Frequency);
        if (budgetTicks <= 0)
        {
            return;
        }

        int budget = Math.Min(Math.Max(0, maxUploadsPerFrame), Math.Max(0, maxRegionsPerFrame));
        if (budget <= 0 || inFlightByRegion.Count <= 0 || levelStates.Length <= 0 || !levelStates[0].HasAnchor)
        {
            return;
        }

        int levels = Math.Clamp(levelStates.Length, 0, Math.Min(8, resources.Levels));
        if (levels <= 0)
        {
            return;
        }

        int resolution = levelStates[0].Resolution;
        uint levelMask = levels >= 32 ? uint.MaxValue : (uint)((1 << levels) - 1);

        VectorInt3[] originMin = ArrayPool<VectorInt3>.Shared.Rent(levels);
        VectorInt3[] ring = ArrayPool<VectorInt3>.Shared.Rent(levels);

        try
        {
            for (int i = 0; i < levels; i++)
            {
                originMin[i] = levelStates[i].OriginMinCell;
                ring[i] = levelStates[i].Ring;
            }

            while (budget > 0 && inFlightByRegion.Count > 0)
            {
                if (Stopwatch.GetTimestamp() - start > budgetTicks)
                {
                    break;
                }

                int batchCap = Math.Min(budget, 16);

                VectorInt3[] regionCoords = ArrayPool<VectorInt3>.Shared.Rent(batchCap);
                ReadOnlyMemory<uint>[] payloads = ArrayPool<ReadOnlyMemory<uint>>.Shared.Rent(batchCap);
                ulong[] regionKeys = ArrayPool<ulong>.Shared.Rent(batchCap);
                int[] versions = ArrayPool<int>.Shared.Rent(batchCap);

                int count = 0;
                int completedHandled = 0;

                try
                {
                    while (count < batchCap)
                    {
                        if (Stopwatch.GetTimestamp() - start > budgetTicks)
                        {
                            break;
                        }

                        if (!TryDequeueValidCompletion(start, budgetTicks, out InFlightCompletion completion, out InFlightRegion inflight))
                        {
                            break;
                        }

                        inFlightByRegion.Remove(completion.RegionKeyPacked);
                        completedHandled++;

                        ChunkKey ck = new ChunkKey(completion.RegionKeyPacked);
                        ck.Decode(out int rx, out int ry, out int rz);
                        var rc = new VectorInt3(rx, ry, rz);

                        try
                        {
                            ChunkWorkResult<LumonSceneTraceSceneRegionArtifact> res = inflight.Task.GetAwaiter().GetResult();
                            if (TryGetDispatchPayload(res, out ReadOnlyMemory<uint> payload))
                            {
                                regionCoords[count] = rc;
                                regionKeys[count] = completion.RegionKeyPacked;
                                versions[count] = res.RequestedVersion;
                                payloads[count] = payload;
                                count++;
                            }
                            else
                            {
                                regionScheduler.OnRequestCompleted(
                                    ck,
                                    status: res.Status,
                                    requestedVersion: res.RequestedVersion,
                                    nowTick: traceSceneNowMs,
                                    error: res.Error);
                                LumonSceneTraceSceneMetrics.OnRegionCompleted(res.Status);
                            }
                        }
                        catch
                        {
                            regionScheduler.OnRequestCompleted(
                                ck,
                                status: ChunkWorkStatus.Failed,
                                requestedVersion: inflight.Version,
                                nowTick: traceSceneNowMs,
                                error: ChunkWorkError.Unknown);
                            LumonSceneTraceSceneMetrics.OnRegionCompleted(ChunkWorkStatus.Failed);
                        }
                    }

                    if (count <= 0)
                    {
                        // If we consumed some completed work but none produced payloads, keep looping to drain more.
                        // Otherwise, nothing dispatchable this frame.
                        if (completedHandled <= 0)
                        {
                            return;
                        }

                        continue;
                    }

                    int dispatched = gpuDispatcher.UploadAndDispatchBatch(
                        resources: resources,
                        regionCoords: regionCoords.AsSpan(0, count),
                        regionPayloads: payloads.AsSpan(0, count),
                        levels: levels,
                        levelMask: levelMask,
                        originMinCellByLevel: originMin.AsSpan(0, levels),
                        ringByLevel: ring.AsSpan(0, levels),
                        resolution: resolution);

                    LumonSceneTraceSceneMetrics.OnUploaded(
                        regions: dispatched,
                        bytes: (long)dispatched * LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount * sizeof(uint));
                    LumonSceneTraceSceneMetrics.OnDispatched(dispatched);

                    for (int i = 0; i < dispatched; i++)
                    {
                        regionScheduler.OnRequestCompleted(
                            new ChunkKey(regionKeys[i]),
                            status: ChunkWorkStatus.Success,
                            requestedVersion: versions[i],
                            nowTick: traceSceneNowMs);
                        LumonSceneTraceSceneMetrics.OnRegionCompleted(ChunkWorkStatus.Success);
                    }

                    // If the GPU budget/dispatcher shorted us, retry the remainder next frame.
                    for (int i = dispatched; i < count; i++)
                    {
                        regionScheduler.OnRequestCompleted(
                            new ChunkKey(regionKeys[i]),
                            status: ChunkWorkStatus.Canceled,
                            requestedVersion: versions[i],
                            nowTick: traceSceneNowMs);
                        LumonSceneTraceSceneMetrics.OnRegionCompleted(ChunkWorkStatus.Canceled);
                    }

                    budget -= dispatched;
                }
                finally
                {
                    ArrayPool<VectorInt3>.Shared.Return(regionCoords, clearArray: false);
                    ArrayPool<ReadOnlyMemory<uint>>.Shared.Return(payloads, clearArray: true);
                    ArrayPool<ulong>.Shared.Return(regionKeys, clearArray: false);
                    ArrayPool<int>.Shared.Return(versions, clearArray: false);
                }
            }
        }
        finally
        {
            ArrayPool<VectorInt3>.Shared.Return(originMin, clearArray: false);
            ArrayPool<VectorInt3>.Shared.Return(ring, clearArray: false);
        }
    }

    private bool TryDequeueValidCompletion(
        long startTimestamp,
        long budgetTicks,
        out InFlightCompletion completion,
        out InFlightRegion inflight)
    {
        while (completedRegions.TryDequeue(out completion))
        {
            if (Stopwatch.GetTimestamp() - startTimestamp > budgetTicks)
            {
                inflight = default;
                return false;
            }

            if (!inFlightByRegion.TryGetValue(completion.RegionKeyPacked, out inflight))
            {
                continue;
            }

            if (!ReferenceEquals(inflight.Task, completion.Task))
            {
                continue;
            }

            return true;
        }

        completion = default;
        inflight = default;
        return false;
    }

    private static int Wrap(int v, int mod)
    {
        if (mod <= 0) return 0;
        int m = v % mod;
        return m < 0 ? m + mod : m;
    }
}
