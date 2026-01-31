using System;
using System.Buffers;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 22.6: feedback-driven residency (v1).
/// Gathers page requests from the PatchIdGBuffer and allocates physical tiles over time.
/// </summary>
internal sealed class LumonSceneFeedbackUpdateRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 0.9998;
    private const int RenderRangeValue = 1;

    private const int VirtualPagesPerChunk = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
    private const int VirtualPageTableW = LumonSceneVirtualAtlasConstants.VirtualPageTableWidth;

    private const int DefaultMaxRequestsPerFrame = 1024;
    private const int DefaultMaxNewAllocationsPerFrame = 16;

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly GBufferManager gBufferManager;

    private readonly LumonScenePhysicalPoolManager physicalPools = new();
    private readonly LumonSceneFieldGpuResources nearGpu = new(LumonSceneField.Near);

    private GpuComputePipeline? feedbackMarkPipeline;
    private GpuComputePipeline? feedbackCompactPipeline;
    private GpuComputePipeline? captureVoxelPipeline;

    private Texture3D? pageUsageStamp;
    private uint feedbackFrameStamp = 1u;
    private uint compactScanOffset = 0u;

    private VectorInt3 slotOriginMinChunk;
    private VectorInt3 slotDims;
    private VectorInt3 slotRing;
    private VectorInt3[] slotOwners = Array.Empty<VectorInt3>();
    private System.Collections.Generic.Dictionary<VectorInt3, uint> chunkToSlot = new();
    private ushort[] slotGenerations = Array.Empty<ushort>();
    private Texture2D? slotGenerationTex;
    private VectorInt3 lastAnchorChunk;

    private LumonScenePageTableEntry[] pageTableMirror = Array.Empty<LumonScenePageTableEntry>();
    private System.Collections.Generic.Dictionary<ulong, uint> virtualToPhysical = new();
    private System.Collections.Generic.Dictionary<uint, ulong> physicalToVirtual = new();

    private LumonSceneFeedbackRequestProcessor? cpuProcessor;

    private bool configured;
    private int lastPlanHash;


    private uint lastRequestCount;
    private int lastRequestsRead;
    private int lastRequestsProcessed;
    private int lastCaptureCount;
    private int lastRelightCount;
    private LumonSceneFeedbackRequestProcessor.ProcessStats lastProcessStats;
    private int lastUniqueVirtualPages;
    private uint lastTopChunkSlot;
    private int lastTopVirtualPage;
    private int lastTopVirtualPageCount;

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

        return true;
    }

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
        UploadPageTableEntryMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: virtualPageIndex, updated.Packed);
        return true;
    }

    internal bool TryClearNearPageFlagsMip0(int virtualPageIndex, LumonScenePageTableEntryPacking.Flags flagsToClear)
        => TryClearNearPageFlagsMip0(chunkSlot: 0u, virtualPageIndex, flagsToClear);

    internal bool TryGetNearDebugSamplingState(
        out Texture3D pageTableMip0,
        out Texture3D irradianceAtlas,
        out int tileSizeTexels,
        out int tilesPerAxis,
        out int tilesPerAtlas)
    {
        pageTableMip0 = default!;
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
            irradianceAtlas = atlases.IrradianceAtlas;
            tileSizeTexels = physicalPools.Near.Plan.TileSizeTexels;
            tilesPerAxis = physicalPools.Near.Plan.TilesPerAxis;
            tilesPerAtlas = physicalPools.Near.Plan.TilesPerAtlas;
            return pageTableMip0.IsValid && irradianceAtlas.IsValid;
        }
        catch
        {
            return false;
        }
    }

    public void NotifyAllDirty(string reason)
    {
        _ = reason;
        Interlocked.Exchange(ref recaptureAllRequested, 1);
    }

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    public LumonSceneFeedbackUpdateRenderer(ICoreClientAPI capi, VgeConfig config, GBufferManager gBufferManager)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.gBufferManager = gBufferManager ?? throw new ArgumentNullException(nameof(gBufferManager));

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
        UpdateSlotWindowForNextFrame();

        EnsurePageUsageStampCreated();

        if (!EnsureFeedbackMarkPipeline() || !EnsureFeedbackCompactPipeline())
        {
            return;
        }

        uint frameStamp = feedbackFrameStamp++;
        if (frameStamp == 0u) frameStamp = feedbackFrameStamp++; // avoid 0 as a valid stamp

        // Pass A: mark visible pages into a dedup stamp texture.
        using (feedbackMarkPipeline!.UseScope())
        {
            _ = feedbackMarkPipeline.ProgramLayout.TryBindSamplerTexture(
                samplerUniformName: "vge_patchIdGBuffer",
                target: TextureTarget.Texture2D,
                textureId: patchIdTex,
                samplerId: 0);

            if (slotGenerationTex is not null && slotGenerationTex.IsValid)
            {
                _ = feedbackMarkPipeline.ProgramLayout.TryBindSamplerTexture(
                    samplerUniformName: "vge_chunkSlotGenerationTex",
                    target: TextureTarget.Texture2D,
                    textureId: slotGenerationTex.TextureId,
                    samplerId: 0);
            }

            _ = feedbackMarkPipeline.ProgramLayout.TryBindImageTexture(
                imageUniformName: "vge_pageUsageStamp",
                texture: pageUsageStamp!,
                access: TextureAccess.ReadWrite,
                level: 0,
                layered: true,
                layer: 0,
                formatOverride: SizedInternalFormat.R32ui);

            _ = feedbackMarkPipeline.TrySetUniform1("vge_frameStamp", frameStamp);

            int gx = (capi.Render.FrameWidth + 7) / 8;
            int gy = (capi.Render.FrameHeight + 7) / 8;
            GL.DispatchCompute(gx, gy, 1);
        }

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);

        // Pass B: compact stamps -> bounded request list.
        nearGpu.PageRequests.Reset();
        using (feedbackCompactPipeline!.UseScope())
        {
            _ = feedbackCompactPipeline.ProgramLayout.TryBindImageTexture(
                imageUniformName: "vge_pageUsageStamp",
                texture: pageUsageStamp!,
                access: TextureAccess.ReadOnly,
                level: 0,
                layered: true,
                layer: 0,
                formatOverride: SizedInternalFormat.R32ui);

            nearGpu.PageRequests.Counter.BindBase(bindingIndex: 0);
            nearGpu.PageRequests.Items.BindBase(bindingIndex: 0);

            _ = feedbackCompactPipeline.TrySetUniform1("vge_maxRequests", (uint)nearGpu.PageRequests.CapacityItems);
            _ = feedbackCompactPipeline.TrySetUniform1("vge_frameStamp", frameStamp);

            int chunkSlotCount = nearGpu.PageTable.ChunkSlotCount;
            int totalEntries = checked(VirtualPagesPerChunk * Math.Max(1, chunkSlotCount));
            compactScanOffset = totalEntries <= 0 ? 0u : (compactScanOffset + (uint)VirtualPagesPerChunk) % (uint)totalEntries;
            _ = feedbackCompactPipeline.TrySetUniform1("vge_scanOffset", compactScanOffset);

            int gx = (totalEntries + 255) / 256;
            GL.DispatchCompute(gx, 1, 1);
        }

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit);

        int captureCount = ProcessRequestsCpu(
            maxRequestsToProcess: DefaultMaxRequestsPerFrame,
            maxNewAllocations: DefaultMaxNewAllocationsPerFrame);

        if (captureCount > 0)
        {
            DispatchVoxelCapture(captureCount);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
            FinalizeCaptureFlagsFromGpuQueue(captureCount);
        }
    }

    public void Dispose()
    {
        capi.Event.LeaveWorld -= OnLeaveWorld;

        feedbackMarkPipeline?.Dispose();
        feedbackMarkPipeline = null;

        feedbackCompactPipeline?.Dispose();
        feedbackCompactPipeline = null;

        captureVoxelPipeline?.Dispose();
        captureVoxelPipeline = null;

        pageUsageStamp?.Dispose();
        pageUsageStamp = null;

        nearGpu.Dispose();
    }

    private void OnLeaveWorld()
    {
        configured = false;
        lastPlanHash = 0;
        feedbackFrameStamp = 1u;
        compactScanOffset = 0u;

        Array.Clear(pageTableMirror);
        virtualToPhysical.Clear();
        physicalToVirtual.Clear();

        Array.Clear(slotOwners);
        chunkToSlot.Clear();
        Array.Clear(slotGenerations);
        slotGenerationTex?.Dispose();
        slotGenerationTex = null;

        slotOriginMinChunk = default;
        slotDims = default;
        slotRing = default;
        lastAnchorChunk = default;
        LumonSceneChunkSlotUniformState.Disable();

        ResetRecaptureList();
    }

    private void EnsureConfigured()
    {
        var cfg = config.LumOn.LumonScene;

        // v1: Near field only (chunkSlotCount=1). Far field integration comes later.
        physicalPools.ConfigureFrom(cfg, maxAtlasCount: LumonScenePhysicalPoolManager.MaxAtlasCountDefault);

        int planHash = HashCode.Combine(
            cfg.NearTexelsPerVoxelFaceEdge,
            cfg.NearRadiusChunks,
            cfg.NearRadiusYChunks,
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
        EnsurePageUsageStampCreated();

        pageTableMirror = new LumonScenePageTableEntry[checked(VirtualPagesPerChunk * chunkSlotCount)];
        virtualToPhysical = new System.Collections.Generic.Dictionary<ulong, uint>(capacity: Math.Max(16, chunkSlotCount));
        physicalToVirtual = new System.Collections.Generic.Dictionary<uint, ulong>(capacity: Math.Max(16, chunkSlotCount));

        EnsureSlotStateConfigured(chunkSlotCount);

        cpuProcessor = new LumonSceneFeedbackRequestProcessor(
            physicalPools.Near,
            pageTableMirror,
            virtualToPhysical,
            physicalToVirtual,
            new RendererPageTableWriter(this));

        ResetRecaptureList();

        // Best-effort clear GPU page table to zeros.
        nearGpu.PageTable.EnsureCreated();
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
            var player = capi.World?.Player;
            var entity = player?.Entity;
            if (entity is null)
            {
                return false;
            }

            // Entity positions are in block units.
            double x = entity.Pos.X;
            double y = entity.Pos.Y;
            double z = entity.Pos.Z;

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
                    pageTableMirror[mirrorIndex] = default;
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

    private bool EnsureFeedbackMarkPipeline()
    {
        if (feedbackMarkPipeline is not null && feedbackMarkPipeline.IsValid)
        {
            return true;
        }

        feedbackMarkPipeline?.Dispose();
        feedbackMarkPipeline = null;

        var loc = AssetLocation.Create("shaders/lumonscene_feedback_mark_pages.csh", ShaderImportsSystem.DefaultDomain);
        IAsset? asset = capi.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            capi.Logger.Warning("[VGE] Missing LumonScene feedback mark shader asset: {0}", loc);
            return false;
        }

        string src = asset.ToText();

        if (!GpuComputePipeline.TryCompileAndCreateGlslPreprocessed(
            api: capi,
            shaderName: "lumonscene_feedback_mark_pages",
            glslSource: src,
            pipeline: out feedbackMarkPipeline,
            sourceCode: out _,
            infoLog: out string infoLog,
            stageExtension: "csh",
            defines: null,
            debugName: "LumOn.LumonScene.FeedbackMarkPages",
            log: capi.Logger))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene feedback mark compute shader: {0}", infoLog);
            feedbackMarkPipeline = null;
            return false;
        }

        return feedbackMarkPipeline is not null;
    }

    private bool EnsureFeedbackCompactPipeline()
    {
        if (feedbackCompactPipeline is not null && feedbackCompactPipeline.IsValid)
        {
            return true;
        }

        feedbackCompactPipeline?.Dispose();
        feedbackCompactPipeline = null;

        var loc = AssetLocation.Create("shaders/lumonscene_feedback_compact_pages.csh", ShaderImportsSystem.DefaultDomain);
        IAsset? asset = capi.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            capi.Logger.Warning("[VGE] Missing LumonScene feedback compact shader asset: {0}", loc);
            return false;
        }

        string src = asset.ToText();

        if (!GpuComputePipeline.TryCompileAndCreateGlslPreprocessed(
            api: capi,
            shaderName: "lumonscene_feedback_compact_pages",
            glslSource: src,
            pipeline: out feedbackCompactPipeline,
            sourceCode: out _,
            infoLog: out string infoLog,
            stageExtension: "csh",
            defines: null,
            debugName: "LumOn.LumonScene.FeedbackCompactPages",
            log: capi.Logger))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene feedback compact compute shader: {0}", infoLog);
            feedbackCompactPipeline = null;
            return false;
        }

        return feedbackCompactPipeline is not null;
    }

    private bool EnsureCaptureVoxelPipeline()
    {
        if (captureVoxelPipeline is not null && captureVoxelPipeline.IsValid)
        {
            return true;
        }

        captureVoxelPipeline?.Dispose();
        captureVoxelPipeline = null;

        var loc = AssetLocation.Create("shaders/lumonscene_capture_voxel.csh", ShaderImportsSystem.DefaultDomain);
        IAsset? asset = capi.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            capi.Logger.Warning("[VGE] Missing LumonScene capture shader asset: {0}", loc);
            return false;
        }

        string src = asset.ToText();

        if (!GpuComputePipeline.TryCompileAndCreateGlslPreprocessed(
            api: capi,
            shaderName: "lumonscene_capture_voxel",
            glslSource: src,
            pipeline: out captureVoxelPipeline,
            sourceCode: out _,
            infoLog: out string infoLog,
            stageExtension: "csh",
            defines: null,
            debugName: "LumOn.LumonScene.CaptureVoxel",
            log: capi.Logger))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene capture voxel compute shader: {0}", infoLog);
            captureVoxelPipeline = null;
            return false;
        }

        return captureVoxelPipeline is not null;
    }

    private int ProcessRequestsCpu(int maxRequestsToProcess, int maxNewAllocations)
    {
        EnsureRecaptureListIfRequested();

        uint requestCount;
        using (var mapped = nearGpu.PageRequests.Counter.MapRange<uint>(dstOffsetBytes: 0, elementCount: 1, access: MapBufferAccessMask.MapReadBit))
        {
            requestCount = (mapped.IsMapped && mapped.Span.Length > 0) ? mapped.Span[0] : 0u;
        }

        int available = nearGpu.PageRequests.CapacityItems;
        int toRead = (int)Math.Min(requestCount, (uint)available);
        int toProcess = Math.Min(toRead, Math.Max(0, maxRequestsToProcess));
        int maxRecapture = 8;
        int maxCapture = Math.Max(1, maxNewAllocations) + maxRecapture;

        if (toProcess <= 0 && recaptureVirtualPageKeys is null)
        {
            nearGpu.CaptureWork.Reset();
            nearGpu.RelightWork.Reset();
            return 0;
        }

        LumonScenePageRequestGpu[] scratch = ArrayPool<LumonScenePageRequestGpu>.Shared.Rent(Math.Max(1, toProcess));
        LumonSceneCaptureWorkGpu[] captureScratch = ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Rent(maxCapture);
        LumonSceneRelightWorkGpu[] relightScratch = ArrayPool<LumonSceneRelightWorkGpu>.Shared.Rent(maxCapture);

        try
        {
            lastRequestCount = requestCount;
            lastRequestsRead = toRead;
            lastRequestsProcessed = toProcess;
            lastUniqueVirtualPages = 0;
            lastTopChunkSlot = 0u;
            lastTopVirtualPage = 0;
            lastTopVirtualPageCount = 0;

            if (toProcess > 0)
            {
                using (var mappedItems = nearGpu.PageRequests.Items.MapRange<LumonScenePageRequestGpu>(
                    dstOffsetBytes: 0,
                    elementCount: toProcess,
                    access: MapBufferAccessMask.MapReadBit))
                {
                    if (!mappedItems.IsMapped)
                    {
                        return 0;
                    }

                    mappedItems.Span.CopyTo(scratch);
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
            }

            cpuProcessor!.Process(
                requests: scratch.AsSpan(0, toProcess),
                maxRequestsToProcess: maxRequestsToProcess,
                maxNewAllocations: maxNewAllocations,
                recaptureVirtualPageKeys: recaptureVirtualPageKeys is null ? ReadOnlySpan<ulong>.Empty : recaptureVirtualPageKeys.AsSpan(0, recaptureCount),
                recaptureCursor: ref recaptureCursor,
                maxRecapture: maxRecapture,
                captureWorkOut: captureScratch,
                relightWorkOut: relightScratch,
                captureCount: out int captureCount,
                relightCount: out int relightCount,
                stats: out lastProcessStats);

            lastCaptureCount = captureCount;
            lastRelightCount = relightCount;

            if (recaptureVirtualPageKeys is not null && recaptureCursor >= recaptureCount)
            {
                ArrayPool<ulong>.Shared.Return(recaptureVirtualPageKeys, clearArray: false);
                recaptureVirtualPageKeys = null;
                recaptureCount = 0;
                recaptureCursor = 0;
            }

            // v1: CPU-produced work overwrites the queues each frame.
            nearGpu.CaptureWork.ResetAndUpload(captureScratch.AsSpan(0, captureCount));
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
        }
    }

    private void EnsureRecaptureListIfRequested()
    {
        if (Interlocked.Exchange(ref recaptureAllRequested, 0) == 0)
        {
            return;
        }

        ResetRecaptureList();

        int count = virtualToPhysical.Count;
        if (count <= 0)
        {
            return;
        }

        ulong[] pages = ArrayPool<ulong>.Shared.Rent(count);
        int i = 0;
        foreach (ulong key in virtualToPhysical.Keys)
        {
            pages[i++] = key;
        }

        recaptureVirtualPageKeys = pages;
        recaptureCount = i;
        recaptureCursor = 0;
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
    }

    private void DispatchVoxelCapture(int captureCount)
    {
        if (captureCount <= 0)
        {
            return;
        }

        if (!EnsureCaptureVoxelPipeline())
        {
            return;
        }

        var atlases = physicalPools.Near.GpuResources;
        if (atlases is null)
        {
            return;
        }

        int tileSize = physicalPools.Near.Plan.TileSizeTexels;
        int tilesPerAxis = physicalPools.Near.Plan.TilesPerAxis;
        int tilesPerAtlas = physicalPools.Near.Plan.TilesPerAtlas;

        using (captureVoxelPipeline!.UseScope())
        {
            nearGpu.CaptureWork.Items.BindBase(bindingIndex: 0);

            // Bind outputs as layered images (units derived from shader layout(binding=...)).
            _ = captureVoxelPipeline.ProgramLayout.TryBindImageTexture(
                imageUniformName: "vge_depthAtlas",
                texture: atlases.DepthAtlas,
                access: TextureAccess.WriteOnly,
                level: 0,
                layered: true,
                layer: 0,
                formatOverride: SizedInternalFormat.R16f);

            _ = captureVoxelPipeline.ProgramLayout.TryBindImageTexture(
                imageUniformName: "vge_materialAtlas",
                texture: atlases.MaterialAtlas,
                access: TextureAccess.WriteOnly,
                level: 0,
                layered: true,
                layer: 0,
                formatOverride: SizedInternalFormat.Rgba8);

            _ = captureVoxelPipeline.TrySetUniform1("vge_tileSizeTexels", (uint)tileSize);
            _ = captureVoxelPipeline.TrySetUniform1("vge_tilesPerAxis", (uint)tilesPerAxis);
            _ = captureVoxelPipeline.TrySetUniform1("vge_tilesPerAtlas", (uint)tilesPerAtlas);
            _ = captureVoxelPipeline.TrySetUniform1("vge_borderTexels", 0u);

            int gx = (tileSize + 7) / 8;
            int gy = (tileSize + 7) / 8;
            GL.DispatchCompute(gx, gy, captureCount);
        }
    }

    private void FinalizeCaptureFlagsFromGpuQueue(int captureCount)
    {
        if (captureCount <= 0)
        {
            return;
        }

        // Read back the capture work items (small, bounded) so we can clear NeedsCapture flags in the page table.
        LumonSceneCaptureWorkGpu[] items = ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Rent(captureCount);
        try
        {
            using var mapped = nearGpu.CaptureWork.Items.MapRange<LumonSceneCaptureWorkGpu>(0, captureCount, MapBufferAccessMask.MapReadBit);
            if (!mapped.IsMapped)
            {
                return;
            }

            mapped.Span.CopyTo(items.AsSpan(0, captureCount));

            for (int i = 0; i < captureCount; i++)
            {
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
                UploadPageTableEntryMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: vpage, updated.Packed);
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

        int ready = 0;
        int needsCap = 0;
        int needsRel = 0;

        for (int i = 0; i < pageTableMirror.Length; i++)
        {
            var entry = pageTableMirror[i];
            if (LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry) == 0u)
            {
                continue;
            }

            var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
            if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsCapture) != 0) needsCap++;
            if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsRelight) != 0) needsRel++;
            if (LumonScenePageTableEntryPacking.IsReadyForSampling(entry)) ready++;
        }

        line =
            $"LS: req:{lastRequestCount} read:{lastRequestsRead} proc:{lastProcessStats.RequestsConsidered} " +
            $"exist:{lastProcessStats.RequestsAcceptedExisting} new:{lastProcessStats.RequestsAllocatedNew} ev:{lastProcessStats.AllocationEvictions} fail:{lastProcessStats.AllocationFailures} " +
            $"uniq:{lastUniqueVirtualPages} top:{lastTopChunkSlot},{lastTopVirtualPage}:{lastTopVirtualPageCount} " +
            $"res:{residentPages}/{cap} ready:{ready} nc:{needsCap} nr:{needsRel} capQ:{lastCaptureCount} relQ:{lastRelightCount} " +
            $"rc:{lastProcessStats.RecaptureSucceeded}/{lastProcessStats.RecaptureAttempted}";

        return true;
    }
}
