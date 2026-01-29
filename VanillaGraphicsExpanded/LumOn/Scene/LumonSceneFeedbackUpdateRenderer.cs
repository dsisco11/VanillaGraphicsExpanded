using System;
using System.Buffers;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
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

    private GpuComputePipeline? feedbackGatherPipeline;
    private GpuComputePipeline? captureVoxelPipeline;

    private readonly LumonScenePageTableEntry[] pageTableMirror = new LumonScenePageTableEntry[VirtualPagesPerChunk];
    private readonly System.Collections.Generic.Dictionary<int, uint> virtualToPhysical = new();
    private readonly System.Collections.Generic.Dictionary<uint, int> physicalToVirtual = new();

    private bool configured;
    private int lastPlanHash;

    private int recaptureAllRequested;
    private int[]? recaptureVirtualPages;
    private int recaptureCount;
    private int recaptureCursor;

    internal bool TryGetNearDispatchState(
        out LumonScenePhysicalFieldPool nearPool,
        out LumonSceneFieldGpuResources nearFieldGpu,
        out System.Collections.Generic.IReadOnlyDictionary<uint, int> physicalToVirtualNear,
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

    internal bool TryClearNearPageFlagsMip0(int virtualPageIndex, LumonScenePageTableEntryPacking.Flags flagsToClear)
    {
        if ((uint)virtualPageIndex >= (uint)VirtualPagesPerChunk)
        {
            return false;
        }

        var entry = pageTableMirror[virtualPageIndex];
        uint pid = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry);
        if (pid == 0)
        {
            return false;
        }

        var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
        flags &= ~flagsToClear;

        LumonScenePageTableEntry updated = LumonScenePageTableEntryPacking.Pack(pid, flags);
        pageTableMirror[virtualPageIndex] = updated;
        UploadPageTableEntryMip0(chunkSlot: 0, virtualPageIndex: virtualPageIndex, updated.Packed);
        return true;
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

        if (!EnsureFeedbackGatherPipeline())
        {
            return;
        }

        nearGpu.PageRequests.Reset();

        using (feedbackGatherPipeline!.UseScope())
        {
            // Bind inputs/outputs.
            _ = feedbackGatherPipeline.ProgramLayout.TryBindSamplerTexture(
                samplerUniformName: "vge_patchIdGBuffer",
                target: TextureTarget.Texture2D,
                textureId: patchIdTex,
                samplerId: 0);

            nearGpu.PageRequests.Counter.BindBase(bindingIndex: 0);
            nearGpu.PageRequests.Items.BindBase(bindingIndex: 0);

            _ = feedbackGatherPipeline.TrySetUniform1("vge_maxRequests", (uint)nearGpu.PageRequests.CapacityItems);

            int gx = (capi.Render.FrameWidth + 7) / 8;
            int gy = (capi.Render.FrameHeight + 7) / 8;
            GL.DispatchCompute(gx, gy, 1);
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

        feedbackGatherPipeline?.Dispose();
        feedbackGatherPipeline = null;

        captureVoxelPipeline?.Dispose();
        captureVoxelPipeline = null;

        nearGpu.Dispose();
    }

    private void OnLeaveWorld()
    {
        configured = false;
        lastPlanHash = 0;

        Array.Clear(pageTableMirror);
        virtualToPhysical.Clear();
        physicalToVirtual.Clear();

        recaptureVirtualPages = null;
        recaptureCount = 0;
        recaptureCursor = 0;
    }

    private void EnsureConfigured()
    {
        var cfg = config.LumOn.LumonScene;

        // v1: Near field only (chunkSlotCount=1). Far field integration comes later.
        physicalPools.ConfigureFrom(cfg, maxAtlasCount: LumonScenePhysicalPoolManager.MaxAtlasCountDefault);

        int planHash = HashCode.Combine(
            cfg.NearTexelsPerVoxelFaceEdge,
            cfg.NearRadiusChunks,
            physicalPools.Near.Plan.CapacityPages);

        if (configured && planHash == lastPlanHash)
        {
            return;
        }

        lastPlanHash = planHash;
        configured = true;

        // Allocate Near GPU resources sized to the physical capacity; chunk slots are stubbed to 1 for now.
        nearGpu.Configure(chunkSlotCount: 1, physicalPageCapacity: Math.Max(1, physicalPools.Near.Plan.CapacityPages));
        nearGpu.EnsureCreated();

        physicalPools.EnsureGpuResources();

        Array.Clear(pageTableMirror);
        virtualToPhysical.Clear();
        physicalToVirtual.Clear();

        // Best-effort clear GPU page table to zeros.
        nearGpu.PageTable.EnsureCreated();
    }

    private bool EnsureFeedbackGatherPipeline()
    {
        if (feedbackGatherPipeline is not null && feedbackGatherPipeline.IsValid)
        {
            return true;
        }

        feedbackGatherPipeline?.Dispose();
        feedbackGatherPipeline = null;

        var loc = AssetLocation.Create("shaders/lumonscene_feedback_gather.csh", ShaderImportsSystem.DefaultDomain);
        IAsset? asset = capi.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            capi.Logger.Warning("[VGE] Missing LumonScene feedback gather shader asset: {0}", loc);
            return false;
        }

        string src = asset.ToText();

        if (!GpuComputePipeline.TryCompileAndCreateGlslPreprocessed(
            api: capi,
            shaderName: "lumonscene_feedback_gather",
            glslSource: src,
            pipeline: out feedbackGatherPipeline,
            sourceCode: out _,
            infoLog: out string infoLog,
            stageExtension: "csh",
            defines: null,
            debugName: "LumOn.LumonScene.FeedbackGather",
            log: capi.Logger))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene feedback gather compute shader: {0}", infoLog);
            feedbackGatherPipeline = null;
            return false;
        }

        return feedbackGatherPipeline is not null;
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
        if (toProcess <= 0 && recaptureVirtualPages is null)
        {
            nearGpu.CaptureWork.Reset();
            nearGpu.RelightWork.Reset();
            return 0;
        }

        LumonScenePageRequestGpu[] scratch = ArrayPool<LumonScenePageRequestGpu>.Shared.Rent(Math.Max(1, toProcess));

        int maxRecapture = 8;
        int maxCapture = Math.Max(1, maxNewAllocations) + maxRecapture;
        LumonSceneCaptureWorkGpu[] captureScratch = ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Rent(maxCapture);
        LumonSceneRelightWorkGpu[] relightScratch = ArrayPool<LumonSceneRelightWorkGpu>.Shared.Rent(maxCapture);

        try
        {
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

            int newAllocs = 0;
            int captureCount = 0;
            int relightCount = 0;
            for (int i = 0; i < toProcess; i++)
            {
                LumonScenePageRequestGpu req = scratch[i];

                // v1: only chunkSlot==0 supported.
                if (req.ChunkSlot != 0u)
                {
                    continue;
                }

                int vpage = (int)req.VirtualPageIndex;
                if ((uint)vpage >= (uint)VirtualPagesPerChunk)
                {
                    continue;
                }

                uint patchId = req.Flags; // v1: compute encodes original patchId in Flags slot.

                if (virtualToPhysical.TryGetValue(vpage, out uint existing))
                {
                    physicalPools.Near.PagePool.Touch(existing);
                    continue;
                }

                if (newAllocs >= maxNewAllocations)
                {
                    continue;
                }

                if (!TryAllocateOrEvictOne(out LumonScenePhysicalPage page))
                {
                    continue;
                }

                newAllocs++;

                uint physicalPageId = page.PhysicalPageId;
                virtualToPhysical[vpage] = physicalPageId;
                physicalToVirtual[physicalPageId] = vpage;

                LumonScenePageTableEntry entry = LumonScenePageTableEntryPacking.Pack(
                    physicalPageId: physicalPageId,
                    flags: LumonScenePageTableEntryPacking.Flags.Resident
                        | LumonScenePageTableEntryPacking.Flags.NeedsCapture
                        | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

                pageTableMirror[vpage] = entry;
                UploadPageTableEntryMip0(chunkSlot: 0, virtualPageIndex: vpage, entry.Packed);

                if (captureCount < captureScratch.Length)
                {
                    captureScratch[captureCount++] = new LumonSceneCaptureWorkGpu(physicalPageId, chunkSlot: 0, patchId: patchId, virtualPageIndex: (uint)vpage);
                }

                if (relightCount < relightScratch.Length)
                {
                    relightScratch[relightCount++] = new LumonSceneRelightWorkGpu(physicalPageId, chunkSlot: 0, patchId: patchId, virtualPageIndex: (uint)vpage);
                }
            }

            // Dirty-driven recapture: re-capture some resident pages each frame until drained.
            if (recaptureVirtualPages is not null)
            {
                for (int i = 0; i < maxRecapture && recaptureCursor < recaptureCount; i++, recaptureCursor++)
                {
                    int vpage = recaptureVirtualPages[recaptureCursor];
                    if (!virtualToPhysical.TryGetValue(vpage, out uint physicalPageId))
                    {
                        continue;
                    }

                    var existingEntry = pageTableMirror[vpage];
                    uint pid = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(existingEntry);
                    if (pid == 0)
                    {
                        continue;
                    }

                    var flags = LumonScenePageTableEntryPacking.UnpackFlags(existingEntry);
                    flags |= LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.NeedsRelight;
                    LumonScenePageTableEntry updated = LumonScenePageTableEntryPacking.Pack(pid, flags);
                    pageTableMirror[vpage] = updated;
                    UploadPageTableEntryMip0(chunkSlot: 0, virtualPageIndex: vpage, updated.Packed);

                    if (captureCount < captureScratch.Length)
                    {
                        captureScratch[captureCount++] = new LumonSceneCaptureWorkGpu(physicalPageId, chunkSlot: 0, patchId: (uint)vpage, virtualPageIndex: (uint)vpage);
                    }

                    if (relightCount < relightScratch.Length)
                    {
                        relightScratch[relightCount++] = new LumonSceneRelightWorkGpu(physicalPageId, chunkSlot: 0, patchId: (uint)vpage, virtualPageIndex: (uint)vpage);
                    }
                }

                if (recaptureCursor >= recaptureCount)
                {
                    ArrayPool<int>.Shared.Return(recaptureVirtualPages, clearArray: false);
                    recaptureVirtualPages = null;
                    recaptureCount = 0;
                    recaptureCursor = 0;
                }
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

        recaptureVirtualPages = null;
        recaptureCount = 0;
        recaptureCursor = 0;

        int count = virtualToPhysical.Count;
        if (count <= 0)
        {
            return;
        }

        int[] pages = ArrayPool<int>.Shared.Rent(count);
        int i = 0;
        foreach (int vpage in virtualToPhysical.Keys)
        {
            pages[i++] = vpage;
        }

        recaptureVirtualPages = pages;
        recaptureCount = i;
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
                int vpage = (int)items[i].VirtualPageIndex;
                if ((uint)vpage >= (uint)VirtualPagesPerChunk)
                {
                    continue;
                }

                var entry = pageTableMirror[vpage];
                uint pid = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry);
                if (pid == 0)
                {
                    continue;
                }

                var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
                flags &= ~LumonScenePageTableEntryPacking.Flags.NeedsCapture;

                LumonScenePageTableEntry updated = LumonScenePageTableEntryPacking.Pack(pid, flags);
                pageTableMirror[vpage] = updated;
                UploadPageTableEntryMip0(chunkSlot: 0, virtualPageIndex: vpage, updated.Packed);
            }
        }
        finally
        {
            ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Return(items, clearArray: false);
        }
    }

    private bool TryAllocateOrEvictOne(out LumonScenePhysicalPage page)
    {
        if (physicalPools.Near.TryAllocate(out page))
        {
            return true;
        }

        if (!physicalPools.Near.TryGetEvictionCandidate(out uint evictId))
        {
            page = default;
            return false;
        }

        if (physicalToVirtual.TryGetValue(evictId, out int vpage))
        {
            physicalToVirtual.Remove(evictId);
            virtualToPhysical.Remove(vpage);
            pageTableMirror[vpage] = default;
            UploadPageTableEntryMip0(chunkSlot: 0, virtualPageIndex: vpage, packedEntry: 0u);
        }

        physicalPools.Near.Free(evictId);

        return physicalPools.Near.TryAllocate(out page);
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
}
