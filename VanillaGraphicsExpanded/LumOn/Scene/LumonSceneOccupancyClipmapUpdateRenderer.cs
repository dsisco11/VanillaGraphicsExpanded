using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    private LevelState[] levelStates = Array.Empty<LevelState>();

    private int rebuildAllRequested;

    private ChunkProcessingService? chunkProcessing;
    private LumonSceneTraceSceneChunkVersionProvider? chunkVersions;
    private LumonSceneTraceSceneChunkSnapshotSource? snapshotSource;
    private readonly LumonSceneTraceSceneRegionProcessor regionProcessor = new();

    private readonly Queue<RegionRequest> pendingRegionsHigh = new();
    private readonly Queue<RegionRequest> pendingRegionsLow = new();
    private readonly HashSet<ulong> pendingRegionKeys = new();
    private readonly Dictionary<ulong, int> appliedVersionByRegion = new();
    private readonly Dictionary<ulong, InFlightRegion> inFlightByRegion = new();

    private VectorInt3 currentWindowRegionMin;
    private VectorInt3 currentWindowRegionMax;
    private bool hasWindowRegionBounds;
    private int maintenanceCursor;

    private long windowSeedCursor;
    private long windowSeedTotal;
    private bool windowSeedActive;

    private int PendingRegionCount => pendingRegionsHigh.Count + pendingRegionsLow.Count;

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    public LumonSceneOccupancyClipmapGpuResources? Resources => resources;

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

    private readonly record struct RegionRequest(VectorInt3 RegionCoord, int Priority);

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

    public LumonSceneOccupancyClipmapUpdateRenderer(ICoreClientAPI capi, VgeConfig config)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        commonEvents = ((ICoreAPI)capi).Event;

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

        // Keep the request queue topped up until we've seeded the whole window at least once.
        EnqueueWindowSeed(targetQueueLen: Math.Max(16, traceCfg.ClipmapMaxRegionUploadsPerFrame * 8));

        // Maintenance: periodically re-request a few regions even without movement (helps cover late loads/unloads).
        EnqueueMaintenance(traceCfg.ClipmapMaxRegionUploadsPerFrame);

        // Start async region extraction jobs under budget.
        IssueRegionRequests(traceCfg.ClipmapMaxRegionUploadsPerFrame);

        // Consume completed results and dispatch region->clipmap compute writes under budgets.
        DispatchCompleted(traceCfg.ClipmapMaxRegionUploadsPerFrame, traceCfg.ClipmapMaxRegionsDispatchedPerFrame);

        LumonSceneTraceSceneMetrics.SetState(
            queueHighLength: pendingRegionsHigh.Count,
            queueLowLength: pendingRegionsLow.Count,
            inFlight: inFlightByRegion.Count,
            appliedRegions: appliedVersionByRegion.Count);

        if (lightIds.TryCopyAndClearDirtyLut(lightLutUpload))
        {
            resources.LightColorLut.UploadDataImmediate(lightLutUpload);
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

        pendingRegionsHigh.Clear();
        pendingRegionsLow.Clear();
        pendingRegionKeys.Clear();
        appliedVersionByRegion.Clear();
        inFlightByRegion.Clear();
        hasWindowRegionBounds = false;
        maintenanceCursor = 0;
        windowSeedCursor = 0;
        windowSeedTotal = 0;
        windowSeedActive = false;
        windowSeedCursor = 0;
        windowSeedTotal = 0;
        windowSeedActive = false;

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

        pendingRegionsHigh.Clear();
        pendingRegionsLow.Clear();
        pendingRegionKeys.Clear();
        appliedVersionByRegion.Clear();
        inFlightByRegion.Clear();
        hasWindowRegionBounds = false;
        maintenanceCursor = 0;

        chunkProcessing?.Dispose();
        chunkProcessing = null;
        chunkVersions = new LumonSceneTraceSceneChunkVersionProvider();
        snapshotSource = new LumonSceneTraceSceneChunkSnapshotSource(capi, chunkVersions, lightIds);
        chunkProcessing = new ChunkProcessingService(snapshotSource, chunkVersions);

        gpuDispatcher ??= new LumonSceneTraceSceneClipmapGpuBuildDispatcher(capi);
    }

    private void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        _ = chunk;
        _ = reason;

        if (chunkVersions is null)
        {
            return;
        }

        ChunkKey key = ChunkKey.FromChunkCoords(chunkCoord.X, chunkCoord.Y, chunkCoord.Z);
        chunkVersions.MarkDirty(key);
        appliedVersionByRegion.Remove(key.Packed);

        if (hasWindowRegionBounds)
        {
            var rc = new VectorInt3(chunkCoord.X, chunkCoord.Y, chunkCoord.Z);
            if (IsInWindow(rc))
            {
                EnqueueRegion(rc, priority: 1);
            }
        }
    }

    private void RequestRebuildAll()
    {
        chunkVersions?.BumpGlobalGeneration();

        pendingRegionsHigh.Clear();
        pendingRegionsLow.Clear();
        pendingRegionKeys.Clear();
        appliedVersionByRegion.Clear();
        inFlightByRegion.Clear();
        hasWindowRegionBounds = false;
        maintenanceCursor = 0;
        windowSeedCursor = 0;
        windowSeedTotal = 0;
        windowSeedActive = false;
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
        // Clamp Y to valid world chunk coords. VintageStory chunks do not exist below 0.
        // (We don't clamp X/Z because negative chunk coords are valid.)
        VectorInt3 clampedMin = newMin;
        VectorInt3 clampedMax = newMax;
        if (clampedMin.Y < 0) clampedMin = new VectorInt3(clampedMin.X, 0, clampedMin.Z);
        if (clampedMax.Y < 0) clampedMax = new VectorInt3(clampedMax.X, -1, clampedMax.Z);

        if (clampedMax.Y < clampedMin.Y)
        {
            // Entire window lies below the world; disable window bounds until we move into valid range.
            hasWindowRegionBounds = false;
            return;
        }

        VectorInt3 prevMin = currentWindowRegionMin;
        VectorInt3 prevMax = currentWindowRegionMax;
        bool hadPrev = hasWindowRegionBounds;

        if (hadPrev && clampedMin == prevMin && clampedMax == prevMax)
        {
            return;
        }

        currentWindowRegionMin = clampedMin;
        currentWindowRegionMax = clampedMax;
        hasWindowRegionBounds = true;

        // The clipmap is a bounded ring-buffer. When regions leave the current window, their texels are eventually
        // overwritten by other regions; so we must forget "applied" state for regions outside the new window.
        if (hadPrev && (prevMin != currentWindowRegionMin || prevMax != currentWindowRegionMax))
        {
            TrimAppliedAndInFlightToWindow();
        }

        ResetWindowSeed();

        // Seeding happens incrementally each frame via EnqueueWindowSeed().
        _ = enqueueAll;
    }

    private void ResetWindowSeed()
    {
        if (!hasWindowRegionBounds)
        {
            windowSeedCursor = 0;
            windowSeedTotal = 0;
            windowSeedActive = false;
            return;
        }

        long sizeX = (long)currentWindowRegionMax.X - currentWindowRegionMin.X + 1L;
        long sizeY = (long)currentWindowRegionMax.Y - currentWindowRegionMin.Y + 1L;
        long sizeZ = (long)currentWindowRegionMax.Z - currentWindowRegionMin.Z + 1L;

        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
        {
            windowSeedCursor = 0;
            windowSeedTotal = 0;
            windowSeedActive = false;
            return;
        }

        windowSeedCursor = 0;
        windowSeedTotal = checked(sizeX * checked(sizeY * sizeZ));
        windowSeedActive = windowSeedTotal > 0;
    }

    private void EnqueueWindowSeed(int targetQueueLen)
    {
        if (!hasWindowRegionBounds || !windowSeedActive || targetQueueLen <= 0)
        {
            return;
        }

        long sizeX = (long)currentWindowRegionMax.X - currentWindowRegionMin.X + 1L;
        long sizeY = (long)currentWindowRegionMax.Y - currentWindowRegionMin.Y + 1L;
        long sizeZ = (long)currentWindowRegionMax.Z - currentWindowRegionMin.Z + 1L;
        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
        {
            windowSeedActive = false;
            return;
        }

        long strideXY = checked(sizeX * sizeY);

        while (PendingRegionCount < targetQueueLen && windowSeedCursor < windowSeedTotal)
        {
            long idx = windowSeedCursor++;

            int rx = checked(currentWindowRegionMin.X + (int)(idx % sizeX));
            int ry = checked(currentWindowRegionMin.Y + (int)((idx / sizeX) % sizeY));
            int rz = checked(currentWindowRegionMin.Z + (int)(idx / strideXY));

            EnqueueRegion(new VectorInt3(rx, ry, rz), priority: 1);
        }

        if (windowSeedCursor >= windowSeedTotal)
        {
            windowSeedActive = false;
        }
    }

    private void TrimAppliedAndInFlightToWindow()
    {
        if (!hasWindowRegionBounds)
        {
            return;
        }

        // appliedVersionByRegion: drop anything outside window so it will be re-uploaded if it re-enters.
        if (appliedVersionByRegion.Count > 0)
        {
            ulong[] keys = ArrayPool<ulong>.Shared.Rent(appliedVersionByRegion.Count);
            int keyCount = 0;
            try
            {
                foreach (ulong rk in appliedVersionByRegion.Keys)
                {
                    keys[keyCount++] = rk;
                }

                for (int i = 0; i < keyCount; i++)
                {
                    ChunkKey ck = new ChunkKey(keys[i]);
                    ck.Decode(out int rx, out int ry, out int rz);
                    if (!IsInWindow(new VectorInt3(rx, ry, rz)))
                    {
                        appliedVersionByRegion.Remove(keys[i]);
                    }
                }
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(keys, clearArray: false);
            }
        }

        // inFlightByRegion: drop anything outside window so it won't be dispatched (work still completes in background).
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
                        inFlightByRegion.Remove(keys[i]);
                    }
                }
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(keys, clearArray: false);
            }
        }
    }

    private void EnqueueAllInWindow(int priority)
    {
        if (!hasWindowRegionBounds)
        {
            return;
        }

        for (int rz = currentWindowRegionMin.Z; rz <= currentWindowRegionMax.Z; rz++)
        for (int ry = currentWindowRegionMin.Y; ry <= currentWindowRegionMax.Y; ry++)
        for (int rx = currentWindowRegionMin.X; rx <= currentWindowRegionMax.X; rx++)
        {
            EnqueueRegion(new VectorInt3(rx, ry, rz), priority);
        }
    }

    private bool IsInWindow(in VectorInt3 regionCoord)
    {
        return regionCoord.X >= currentWindowRegionMin.X && regionCoord.X <= currentWindowRegionMax.X
            && regionCoord.Y >= currentWindowRegionMin.Y && regionCoord.Y <= currentWindowRegionMax.Y
            && regionCoord.Z >= currentWindowRegionMin.Z && regionCoord.Z <= currentWindowRegionMax.Z;
    }

    private bool EnqueueRegion(in VectorInt3 regionCoord, int priority)
    {
        if (regionCoord.Y < 0)
        {
            return false;
        }

        ChunkKey key = ChunkKey.FromChunkCoords(regionCoord.X, regionCoord.Y, regionCoord.Z);
        if (!pendingRegionKeys.Add(key.Packed))
        {
            return false;
        }

        if (priority > 0)
        {
            pendingRegionsHigh.Enqueue(new RegionRequest(regionCoord, priority));
        }
        else
        {
            pendingRegionsLow.Enqueue(new RegionRequest(regionCoord, priority));
        }
        return true;
    }

    private void EnqueueMaintenance(int maxUploadsPerFrame)
    {
        if (!hasWindowRegionBounds || maxUploadsPerFrame <= 0)
        {
            return;
        }

        if (PendingRegionCount > 0)
        {
            return;
        }

        int sizeX = currentWindowRegionMax.X - currentWindowRegionMin.X + 1;
        int sizeY = currentWindowRegionMax.Y - currentWindowRegionMin.Y + 1;
        int sizeZ = currentWindowRegionMax.Z - currentWindowRegionMin.Z + 1;
        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
        {
            return;
        }

        int total = checked(sizeX * sizeY * sizeZ);
        int want = Math.Min(maxUploadsPerFrame, 2);

        for (int i = 0; i < want; i++)
        {
            int idx = maintenanceCursor++ % total;

            int rx = currentWindowRegionMin.X + (idx % sizeX);
            int ry = currentWindowRegionMin.Y + ((idx / sizeX) % sizeY);
            int rz = currentWindowRegionMin.Z + (idx / (sizeX * sizeY));

            EnqueueRegion(new VectorInt3(rx, ry, rz), priority: 0);
        }
    }

    private void IssueRegionRequests(int maxRequestsPerFrame)
    {
        if (chunkProcessing is null || chunkVersions is null || maxRequestsPerFrame <= 0)
        {
            return;
        }

        int issued = 0;
        while (issued < maxRequestsPerFrame && PendingRegionCount > 0)
        {
            RegionRequest req = pendingRegionsHigh.Count > 0 ? pendingRegionsHigh.Dequeue() : pendingRegionsLow.Dequeue();
            ChunkKey key = ChunkKey.FromChunkCoords(req.RegionCoord.X, req.RegionCoord.Y, req.RegionCoord.Z);
            pendingRegionKeys.Remove(key.Packed);

            if (hasWindowRegionBounds && !IsInWindow(req.RegionCoord))
            {
                continue;
            }

            int version = chunkVersions.GetCurrentVersion(key);
            if (appliedVersionByRegion.TryGetValue(key.Packed, out int applied) && applied == version)
            {
                continue;
            }

            if (inFlightByRegion.TryGetValue(key.Packed, out InFlightRegion existing)
                && existing.Version == version
                && !existing.Task.IsCompleted)
            {
                continue;
            }

            var options = new ChunkWorkOptions { Priority = req.Priority };
            Task<ChunkWorkResult<LumonSceneTraceSceneRegionArtifact>> task =
                chunkProcessing.RequestAsync(key, version, regionProcessor, options);

            inFlightByRegion[key.Packed] = new InFlightRegion(version, task);
            LumonSceneTraceSceneMetrics.OnRegionRequestsIssued(1);
            issued++;
        }
    }

    private void DispatchCompleted(int maxUploadsPerFrame, int maxRegionsPerFrame)
    {
        if (resources is null || gpuDispatcher is null)
        {
            return;
        }

        using var cpuScope = Profiler.BeginScope("LumOn.TraceScene.DispatchCompleted", "Render");

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
                int batchCap = Math.Min(budget, 16);

                VectorInt3[] regionCoords = ArrayPool<VectorInt3>.Shared.Rent(batchCap);
                ReadOnlyMemory<uint>[] payloads = ArrayPool<ReadOnlyMemory<uint>>.Shared.Rent(batchCap);
                ulong[] regionKeys = ArrayPool<ulong>.Shared.Rent(batchCap);
                int[] versions = ArrayPool<int>.Shared.Rent(batchCap);

                int count = 0;
                int completedRemoved = 0;

                try
                {
                    ulong[] keys = ArrayPool<ulong>.Shared.Rent(inFlightByRegion.Count);
                    int keyCount = 0;
                    try
                    {
                        foreach (var kvp in inFlightByRegion)
                        {
                            keys[keyCount++] = kvp.Key;
                        }

                        for (int i = 0; i < keyCount && count < batchCap; i++)
                        {
                            ulong rk = keys[i];
                            if (!inFlightByRegion.TryGetValue(rk, out InFlightRegion inflight))
                            {
                                continue;
                            }

                            if (!inflight.Task.IsCompleted)
                            {
                                continue;
                            }

                            inFlightByRegion.Remove(rk);
                            completedRemoved++;

                            ChunkKey ck = new ChunkKey(rk);
                            ck.Decode(out int rx, out int ry, out int rz);
                            var rc = new VectorInt3(rx, ry, rz);

                            try
                            {
                                ChunkWorkResult<LumonSceneTraceSceneRegionArtifact> res = inflight.Task.GetAwaiter().GetResult();
                                if (TryGetDispatchPayload(res, out ReadOnlyMemory<uint> payload))
                                {
                                    regionCoords[count] = rc;
                                    regionKeys[count] = rk;
                                    versions[count] = inflight.Version;
                                    payloads[count] = payload;
                                    count++;
                                }
                                else
                                {
                                    // No payload available yet (chunk missing/unpacked/canceled/etc) - retry later.
                                    EnqueueRegion(rc, priority: 0);
                                }
                            }
                            catch
                            {
                                EnqueueRegion(rc, priority: 0);
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<ulong>.Shared.Return(keys, clearArray: false);
                    }

                    if (count <= 0)
                    {
                        // If we consumed some completed work but none produced payloads, keep looping to drain more.
                        // Otherwise, nothing dispatchable this frame.
                        if (completedRemoved <= 0)
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
                        appliedVersionByRegion[regionKeys[i]] = versions[i];
                    }

                    // If the GPU budget/dispatcher shorted us, retry the remainder next frame.
                    for (int i = dispatched; i < count; i++)
                    {
                        EnqueueRegion(regionCoords[i], priority: 1);
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

    private static int Wrap(int v, int mod)
    {
        if (mod <= 0) return 0;
        int m = v % mod;
        return m < 0 ? m + mod : m;
    }
}
