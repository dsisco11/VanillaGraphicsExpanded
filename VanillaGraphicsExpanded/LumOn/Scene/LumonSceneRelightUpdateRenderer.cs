using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using System;
using System.Buffers;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 22.9: Per-texel relight compute (v1).
/// Consumes the trace scene (occupancy clipmap) and writes into the surface-cache irradiance atlas with temporal accumulation.
/// </summary>
internal sealed class LumonSceneRelightUpdateRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 0.99986; // after feedback+capture (0.9998)
    private const int RenderRangeValue = 1;

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly LumonSceneFeedbackUpdateRenderer feedback;
    private readonly TraceGeometryRenderer occupancy;

    private LumonSceneRelightVoxelDdaComputeShader? relightVoxelShader;
    private GpuAtomicCounterBuffer? debugCounters;

    private int lastWorkCount;
    private uint lastDbgRays;
    private uint lastDbgHits;
    private uint lastDbgMisses;
    private uint lastDbgOobStarts;
    private int lastClearedNeedsRelightOk;
    private int lastClearedNeedsRelightFail;
    private VectorInt3 lastOccOriginMinCell0;
    private VectorInt3 lastOccRing0;
    private int lastOccResolution;

    private int frameIndex;
    private string lastSelectionMode = string.Empty;

    // When RelightTexelsPerPagePerFrame < tileSize^2, a single dispatch only covers a subset of texels.
    // Track per-virtual-page remaining batches so we don't clear NeedsRelight until the whole tile has been touched.
    private readonly LumonSceneRelightBatchSchedule relightBatches = new();
    private int lastBatchCount = 1;
    private long lastGeometryHistoryRevision = -1;



    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    public LumonSceneRelightUpdateRenderer(
        ICoreClientAPI capi,
        VgeConfig config,
        LumonSceneFeedbackUpdateRenderer feedback,
        TraceGeometryRenderer occupancy)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));

        capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "vge_lumonscene_relight");
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

        var cfg = config.LumOn.LumonScene;

        if (!feedback.TryGetNearDispatchState(
            out var nearPool,
            out var nearGpu,
            out var physicalToVirtual,
            out var pageTableMirrorMip0))
        {
            return;
        }

        var atlases = nearPool.GpuResources;
        if (atlases is null)
        {
            return;
        }

        var occRes = occupancy.PrepareScene();
        if (occRes is null)
        {
            return;
        }

        if (!occupancy.TryGetLevel0RuntimeParams(out VectorInt3 occOriginMinCell0, out VectorInt3 occRing0, out int occResolution))
        {
            return;
        }

        lastOccOriginMinCell0 = occOriginMinCell0;
        lastOccRing0 = occRing0;
        lastOccResolution = occResolution;

        int maxPages = cfg.RelightMaxPagesPerFrame;
        if (maxPages <= 0)
        {
            return;
        }

        if (cfg.RelightTexelsPerPagePerFrame <= 0)
        {
            return;
        }

        int tileSize = nearPool.Plan.TileSizeTexels;
        int tilesPerAxis = nearPool.Plan.TilesPerAxis;
        int tilesPerAtlas = nearPool.Plan.TilesPerAtlas;

        uint totalTexelsU = (uint)(tileSize * tileSize);
        uint kU = (uint)Math.Max(0, cfg.RelightTexelsPerPagePerFrame);
        uint batchCountU = kU == 0u ? 0u : ((totalTexelsU + (kU - 1u)) / kU);
        int batchCount = (int)Math.Clamp(batchCountU, 0u, 1_000_000u);
        if (batchCount <= 0)
        {
            batchCount = 1;
        }

        if (batchCount != lastBatchCount || lastGeometryHistoryRevision != feedback.GeometryHistoryRevision)
        {
            // Batch schedule state depends on batchCount. If config changes, reset state.
            relightBatches.Clear();
            lastBatchCount = batchCount;
            lastGeometryHistoryRevision = feedback.GeometryHistoryRevision;
        }

        // Select pages to relight:
        // - Prefer scheduler-driven selection from active cells with NeedsRelight backlog.
        // - Fallback to MRU selection (legacy behavior) if scheduler work is unavailable.
        int wantIds = Math.Min(checked(maxPages * 4), nearPool.PagePool.CapacityPages);
        uint[] mruIds = ArrayPool<uint>.Shared.Rent(Math.Max(1, wantIds));
        LumonSceneRelightWorkGpu[] work = ArrayPool<LumonSceneRelightWorkGpu>.Shared.Rent(Math.Max(1, maxPages));
        ulong[] workVirtualKeys = ArrayPool<ulong>.Shared.Rent(Math.Max(1, maxPages));

        int workCount = 0;
        int mruWritten = 0;

        try
        {
            bool usedScheduler = feedback.TryBuildNearRelightWorkFromScheduler(
                maxPages: maxPages,
                workOut: work.AsSpan(0, maxPages),
                workVirtualKeysOut: workVirtualKeys.AsSpan(0, maxPages),
                workCount: out workCount);

            if (usedScheduler)
            {
                lastSelectionMode = "sched";
            }

            // Fallback: MRU selection.
            if (!usedScheduler || workCount <= 0)
            {
                lastSelectionMode = "mru";
                workCount = 0;

                mruWritten = nearPool.PagePool.CopyMostRecentlyUsed(mruIds.AsSpan(0, wantIds));
                for (int i = 0; i < mruWritten && workCount < maxPages; i++)
                {
                    uint physicalPageId = mruIds[i];
                    if (physicalPageId == 0u)
                    {
                        continue;
                    }

                    if (!physicalToVirtual.TryGetValue(physicalPageId, out ulong key))
                    {
                        continue;
                    }

                    uint chunkSlot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
                    int vpage = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);

                    if ((uint)vpage >= (uint)LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk)
                    {
                        continue;
                    }

                    // Filter to NeedsRelight pages when using MRU, otherwise we waste budget.
                    int mirrorIndex = checked((int)chunkSlot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk + vpage);
                    if ((uint)mirrorIndex >= (uint)pageTableMirrorMip0.Length)
                    {
                        continue;
                    }

                    var entry = pageTableMirrorMip0[mirrorIndex];
                    var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
                    if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsRelight) == 0)
                    {
                        continue;
                    }

                    if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsCapture) != 0)
                    {
                        continue;
                    }

                    uint virtualPageIndex = (uint)vpage;
                    work[workCount] = new LumonSceneRelightWorkGpu(physicalPageId, chunkSlot: chunkSlot, patchId: virtualPageIndex, virtualPageIndex: virtualPageIndex);
                    workVirtualKeys[workCount] = key;
                    workCount++;
                }
            }

            if (workCount <= 0)
            {
                return;
            }

            lastWorkCount = workCount;

            // Ensure per-page batch selection is deterministic and progresses across frames.
            // We use the WorkGpu.PatchId field as `batchIndex` for the shader (v1 placeholder; PatchId is otherwise unused here).
            for (int i = 0; i < workCount; i++)
            {
                ulong key = workVirtualKeys[i];
                uint batchIndex = relightBatches.Next(key, batchCount);
                var w = work[i];
                work[i] = new LumonSceneRelightWorkGpu(
                    physicalPageId: w.PhysicalPageId,
                    chunkSlot: w.ChunkSlot,
                    patchId: batchIndex,
                    virtualPageIndex: w.VirtualPageIndex);
            }

            // Upload work buffer for GPU consumption.
            nearGpu.RelightWork.ResetAndUpload(work.AsSpan(0, workCount));

            if (!EnsureRelightVoxelShader())
            {
                return;
            }

            using (relightVoxelShader!.UseScope())
            {
                using var gpuScope = GlGpuProfiler.Instance.Scope("Relight.VoxelDda");

                bool dbg = config.Debug.LumOnRuntimeSelfCheckEnabled;
                EnsureDebugCountersCreated();

                // Bridge UBO: required to convert matrix-space patch positions into absolute world-cell coords for occupancy sampling.
                var terrainBridgeUbo = LumOnTerrainBridgeUboState.UboOrNull;
                if (terrainBridgeUbo is not null)
                {
                    relightVoxelShader.BindTerrainBridgeUbo(terrainBridgeUbo);
                }

                relightVoxelShader.BindRelightWorkSsbo(nearGpu.RelightWork.Items);
                relightVoxelShader.BindPatchMetaSsbo(nearGpu.PatchMetadata.Ssbo);

                relightVoxelShader.BindDepthAtlas(atlases.DepthAtlasTextureId);
                relightVoxelShader.BindMaterialAtlas(atlases.MaterialAtlasTextureId);

                relightVoxelShader.BindSharedGeometry(occRes);
                relightVoxelShader.BindLightColorLut(occRes.LightColors.TextureId);
                relightVoxelShader.BindBlockLevelScalarLut(occRes.BlockLevels.TextureId);
                relightVoxelShader.BindSunLevelScalarLut(occRes.SunLevels.TextureId);
                relightVoxelShader.BindMaterialPalette(occRes.Faces.TextureId);
                relightVoxelShader.BindSurfaceLut(occRes.Surfaces.TextureId);

                relightVoxelShader.BindIrradianceAtlasImage(atlases.IrradianceAtlas, access: TextureAccess.ReadWrite);

                relightVoxelShader.SetAtlasLayout(
                    tileSizeTexels: (uint)tileSize,
                    tilesPerAxis: (uint)tilesPerAxis,
                    tilesPerAtlas: (uint)tilesPerAtlas,
                    borderTexels: 0u);

                relightVoxelShader.SetRelightParams(
                    frameIndex: frameIndex,
                    texelsPerPagePerFrame: (uint)Math.Max(0, cfg.RelightTexelsPerPagePerFrame),
                    raysPerTexel: (uint)Math.Max(0, cfg.RelightRaysPerTexel),
                    maxDdaSteps: (uint)Math.Max(0, cfg.RelightMaxDdaSteps),
                    debugCountersEnabled: dbg);

                relightVoxelShader.SetOccupancyMapping(
                    originMinCellX: occOriginMinCell0.X,
                    originMinCellY: occOriginMinCell0.Y,
                    originMinCellZ: occOriginMinCell0.Z,
                    ringX: occRing0.X,
                    ringY: occRing0.Y,
                    ringZ: occRing0.Z,
                    resolution: occResolution);

                if (debugCounters is not null && debugCounters.IsValid)
                {
                    Span<uint> zero = stackalloc uint[4] { 0u, 0u, 0u, 0u };
                    debugCounters.UploadSubData((ReadOnlySpan<uint>)zero, dstOffsetBytes: 0);
                    relightVoxelShader.BindDebugCounters(debugCounters);
                }

                int gx = (tileSize + 7) / 8;
                int gy = (tileSize + 7) / 8;
                GL.DispatchCompute(gx, gy, workCount);
            }

            // Relight writes irradiance via imageStore, then the same texture is sampled as a sampler2DArray
            // by later passes (debug/combining). Ensure visibility for both image access and texture fetch.
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

            if (config.Debug.LumOnRuntimeSelfCheckEnabled && debugCounters is not null && debugCounters.IsValid)
            {
                using var mapped = debugCounters.MapRange<uint>(dstOffsetBytes: 0, elementCount: 4, access: MapBufferAccessMask.MapReadBit);
                if (mapped.IsMapped && mapped.Span.Length >= 4)
                {
                    lastDbgRays = mapped.Span[0];
                    lastDbgHits = mapped.Span[1];
                    lastDbgMisses = mapped.Span[2];
                    lastDbgOobStarts = mapped.Span[3];
                }
            }

            // v1: clear NeedsRelight flags for the pages we just touched (best-effort).
            // Scheduling is MRU-based so pages will continue to accumulate even without the flag.
            int clearOk = 0;
            int clearFail = 0;

            // Failed texels keep their page retryable; a dispatch is not evidence of a complete lighting sample.
            using var completedWork = nearGpu.RelightWork.Items.MapRange<LumonSceneRelightWorkGpu>(0, workCount, MapBufferAccessMask.MapReadBit);
            if (!completedWork.IsMapped) return;

            for (int i = 0; i < workCount; i++)
            {
                ulong key = workVirtualKeys[i];
                bool succeeded = (completedWork.Span[i].VirtualPageIndex & 0x80000000u) == 0;
                if (!relightBatches.Complete(key, batchCount, succeeded)) continue;

                uint chunkSlot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
                int vpage = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
                if (feedback.TryClearNearPageFlagsMip0(chunkSlot, vpage, LumonScenePageTableEntryPacking.Flags.NeedsRelight))
                {
                    clearOk++;
                }
                else
                {
                    clearFail++;
                }
            }

            lastClearedNeedsRelightOk = clearOk;
            lastClearedNeedsRelightFail = clearFail;

            frameIndex = unchecked(frameIndex + 1);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(mruIds, clearArray: false);
            ArrayPool<LumonSceneRelightWorkGpu>.Shared.Return(work, clearArray: false);
            ArrayPool<ulong>.Shared.Return(workVirtualKeys, clearArray: false);
        }
    }

    public void Dispose()
    {
        capi.Event.LeaveWorld -= OnLeaveWorld;

        relightVoxelShader?.Dispose();
        relightVoxelShader = null;

        debugCounters?.Dispose();
        debugCounters = null;
    }

    private void OnLeaveWorld()
    {
        frameIndex = 0;
        lastWorkCount = 0;
        lastDbgRays = 0;
        lastDbgHits = 0;
        lastDbgMisses = 0;
        lastDbgOobStarts = 0;
        lastClearedNeedsRelightOk = 0;
        lastClearedNeedsRelightFail = 0;
        relightBatches.Clear();
        lastBatchCount = 1;
        relightVoxelShader?.Dispose();
        relightVoxelShader = null;

        debugCounters?.Dispose();
        debugCounters = null;
    }

    private void EnsureDebugCountersCreated()
    {
        if (debugCounters is not null && debugCounters.IsValid)
        {
            return;
        }

        debugCounters?.Dispose();
        debugCounters = GpuAtomicCounterBuffer.Create(BufferUsageHint.DynamicDraw, debugName: "LumOn.LumonScene.Relight.DebugCounters(ACBO)");
        debugCounters.InitializeCounters(counterCount: 4, initialValue: 0);
    }

    internal bool TryGetSelfCheckLine(out string line)
    {
        line = string.Empty;
        if (!config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled)
        {
            return false;
        }

        line =
            $"LSR: mode:{lastSelectionMode} pages:{lastWorkCount} clr:{lastClearedNeedsRelightOk}/{lastWorkCount} fail:{lastClearedNeedsRelightFail} " +
            $"rays:{lastDbgRays} hit:{lastDbgHits} miss:{lastDbgMisses} oob0:{lastDbgOobStarts} " +
            $"occ0:({lastOccOriginMinCell0.X},{lastOccOriginMinCell0.Y},{lastOccOriginMinCell0.Z}) r:({lastOccRing0.X},{lastOccRing0.Y},{lastOccRing0.Z}) res:{lastOccResolution}";
        return true;
    }

    private bool EnsureRelightVoxelShader()
    {
        if (relightVoxelShader is not null && relightVoxelShader.IsValid)
        {
            return true;
        }

        relightVoxelShader?.Dispose();
        relightVoxelShader = null;

        if (!LumonSceneRelightVoxelDdaComputeShader.TryCreate(
            api: capi,
            shader: out relightVoxelShader,
            infoLog: out string infoLog,
            preferSpirv: true,
            debugName: "LumOn.LumonScene.RelightVoxelDda"))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene relight compute shader: {0}", infoLog);
            relightVoxelShader = null;
            return false;
        }

        return relightVoxelShader is not null;
    }

}
