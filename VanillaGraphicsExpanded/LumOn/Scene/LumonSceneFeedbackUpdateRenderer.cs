using System;
using System.Buffers;

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

    private readonly LumonScenePageTableEntry[] pageTableMirror = new LumonScenePageTableEntry[VirtualPagesPerChunk];
    private readonly System.Collections.Generic.Dictionary<int, uint> virtualToPhysical = new();
    private readonly System.Collections.Generic.Dictionary<uint, int> physicalToVirtual = new();

    private bool configured;
    private int lastPlanHash;

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

            int locMax = GL.GetUniformLocation(feedbackGatherPipeline.ProgramId, "vge_maxRequests");
            if (locMax >= 0)
            {
                GL.Uniform1(locMax, nearGpu.PageRequests.CapacityItems);
            }

            int gx = (capi.Render.FrameWidth + 7) / 8;
            int gy = (capi.Render.FrameHeight + 7) / 8;
            GL.DispatchCompute(gx, gy, 1);
        }

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit);

        ProcessRequestsCpu(
            maxRequestsToProcess: DefaultMaxRequestsPerFrame,
            maxNewAllocations: DefaultMaxNewAllocationsPerFrame);
    }

    public void Dispose()
    {
        capi.Event.LeaveWorld -= OnLeaveWorld;

        feedbackGatherPipeline?.Dispose();
        feedbackGatherPipeline = null;

        nearGpu.Dispose();
    }

    private void OnLeaveWorld()
    {
        configured = false;
        lastPlanHash = 0;

        Array.Clear(pageTableMirror);
        virtualToPhysical.Clear();
        physicalToVirtual.Clear();
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

    private void ProcessRequestsCpu(int maxRequestsToProcess, int maxNewAllocations)
    {
        uint requestCount;
        using (var mapped = nearGpu.PageRequests.Counter.MapRange<uint>(dstOffsetBytes: 0, elementCount: 1, access: MapBufferAccessMask.MapReadBit))
        {
            requestCount = (mapped.IsMapped && mapped.Span.Length > 0) ? mapped.Span[0] : 0u;
        }

        int available = nearGpu.PageRequests.CapacityItems;
        int toRead = (int)Math.Min(requestCount, (uint)available);
        int toProcess = Math.Min(toRead, Math.Max(0, maxRequestsToProcess));
        if (toProcess <= 0)
        {
            return;
        }

        LumonScenePageRequestGpu[] scratch = ArrayPool<LumonScenePageRequestGpu>.Shared.Rent(toProcess);
        LumonSceneCaptureWorkGpu[] captureScratch = ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Rent(Math.Max(1, maxNewAllocations));
        LumonSceneRelightWorkGpu[] relightScratch = ArrayPool<LumonSceneRelightWorkGpu>.Shared.Rent(Math.Max(1, maxNewAllocations));
        try
        {
            using (var mappedItems = nearGpu.PageRequests.Items.MapRange<LumonScenePageRequestGpu>(
                dstOffsetBytes: 0,
                elementCount: toProcess,
                access: MapBufferAccessMask.MapReadBit))
            {
                if (!mappedItems.IsMapped)
                {
                    return;
                }

                mappedItems.Span.CopyTo(scratch);
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

            // v1: CPU-produced work overwrites the queues each frame.
            nearGpu.CaptureWork.ResetAndUpload(captureScratch.AsSpan(0, captureCount));
            nearGpu.RelightWork.ResetAndUpload(relightScratch.AsSpan(0, relightCount));
        }
        finally
        {
            ArrayPool<LumonScenePageRequestGpu>.Shared.Return(scratch, clearArray: false);
            ArrayPool<LumonSceneCaptureWorkGpu>.Shared.Return(captureScratch, clearArray: false);
            ArrayPool<LumonSceneRelightWorkGpu>.Shared.Return(relightScratch, clearArray: false);
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
