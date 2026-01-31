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

    private GpuComputePipeline? feedbackMarkPipeline;
    private GpuComputePipeline? feedbackCompactPipeline;
    private GpuComputePipeline? captureVoxelPipeline;

    private Texture2D? pageUsageStamp;
    private uint feedbackFrameStamp = 1u;

    private readonly LumonScenePageTableEntry[] pageTableMirror = new LumonScenePageTableEntry[VirtualPagesPerChunk];
    private readonly System.Collections.Generic.Dictionary<int, uint> virtualToPhysical = new();
    private readonly System.Collections.Generic.Dictionary<uint, int> physicalToVirtual = new();

    private readonly LumonSceneFeedbackRequestProcessor cpuProcessor;

    private bool configured;
    private int lastPlanHash;


    private uint lastRequestCount;
    private int lastRequestsRead;
    private int lastRequestsProcessed;
    private int lastCaptureCount;
    private int lastRelightCount;
    private LumonSceneFeedbackRequestProcessor.ProcessStats lastProcessStats;
    private int lastUniqueVirtualPages;
    private int lastTopVirtualPage;
    private int lastTopVirtualPageCount;

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

        cpuProcessor = new LumonSceneFeedbackRequestProcessor(
            physicalPools.Near,
            pageTableMirror,
            virtualToPhysical,
            physicalToVirtual,
            new RendererPageTableWriter(this));

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

            _ = feedbackMarkPipeline.ProgramLayout.TryBindImageTexture(
                imageUniformName: "vge_pageUsageStamp",
                texture: pageUsageStamp!,
                access: TextureAccess.ReadWrite,
                level: 0,
                layered: false,
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
                layered: false,
                layer: 0,
                formatOverride: SizedInternalFormat.R32ui);

            nearGpu.PageRequests.Counter.BindBase(bindingIndex: 0);
            nearGpu.PageRequests.Items.BindBase(bindingIndex: 0);

            _ = feedbackCompactPipeline.TrySetUniform1("vge_maxRequests", (uint)nearGpu.PageRequests.CapacityItems);
            _ = feedbackCompactPipeline.TrySetUniform1("vge_frameStamp", frameStamp);

            int gx = (VirtualPagesPerChunk + 255) / 256;
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
        EnsurePageUsageStampCreated();

        Array.Clear(pageTableMirror);
        virtualToPhysical.Clear();
        physicalToVirtual.Clear();

        // Best-effort clear GPU page table to zeros.
        nearGpu.PageTable.EnsureCreated();
    }

    private void EnsurePageUsageStampCreated()
    {
        if (pageUsageStamp is not null && pageUsageStamp.IsValid)
        {
            return;
        }

        pageUsageStamp?.Dispose();
        pageUsageStamp = Texture2D.Create(
            width: LumonSceneVirtualAtlasConstants.VirtualPageTableWidth,
            height: LumonSceneVirtualAtlasConstants.VirtualPageTableHeight,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            debugName: $"LumOn.LumonScene.{LumonSceneField.Near}.PageUsageStamp(R32UI)");

        // Clear to 0 once; stamps use frameStamp!=0 to avoid needing per-frame clears.
        pageUsageStamp.UploadDataImmediate(new uint[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk]);
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

        if (toProcess <= 0 && recaptureVirtualPages is null)
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
                var counts = new System.Collections.Generic.Dictionary<int, int>();
                int topVpage = 0;
                int topCount = 0;

                for (int i = 0; i < toProcess; i++)
                {
                    int vpage = (int)scratch[i].VirtualPageIndex;
                    if ((uint)vpage >= (uint)VirtualPagesPerChunk)
                    {
                        continue;
                    }

                    int c = 1;
                    if (counts.TryGetValue(vpage, out int existing))
                    {
                        c = existing + 1;
                    }
                    counts[vpage] = c;

                    if (c > topCount)
                    {
                        topCount = c;
                        topVpage = vpage;
                    }
                }

                lastUniqueVirtualPages = counts.Count;
                lastTopVirtualPage = topVpage;
                lastTopVirtualPageCount = topCount;
            }

            cpuProcessor.Process(
                requests: scratch.AsSpan(0, toProcess),
                maxRequestsToProcess: maxRequestsToProcess,
                maxNewAllocations: maxNewAllocations,
                recaptureVirtualPages: recaptureVirtualPages is null ? ReadOnlySpan<int>.Empty : recaptureVirtualPages.AsSpan(0, recaptureCount),
                recaptureCursor: ref recaptureCursor,
                maxRecapture: maxRecapture,
                captureWorkOut: captureScratch,
                relightWorkOut: relightScratch,
                captureCount: out int captureCount,
                relightCount: out int relightCount,
                stats: out lastProcessStats);

            lastCaptureCount = captureCount;
            lastRelightCount = relightCount;

            if (recaptureVirtualPages is not null && recaptureCursor >= recaptureCount)
            {
                ArrayPool<int>.Shared.Return(recaptureVirtualPages, clearArray: false);
                recaptureVirtualPages = null;
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
            $"uniq:{lastUniqueVirtualPages} top:{lastTopVirtualPage}:{lastTopVirtualPageCount} " +
            $"res:{residentPages}/{cap} ready:{ready} nc:{needsCap} nr:{needsRel} capQ:{lastCaptureCount} relQ:{lastRelightCount} " +
            $"rc:{lastProcessStats.RecaptureSucceeded}/{lastProcessStats.RecaptureAttempted}";

        return true;
    }
}
