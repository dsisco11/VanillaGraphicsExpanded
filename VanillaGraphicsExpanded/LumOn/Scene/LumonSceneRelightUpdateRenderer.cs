using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Produces and publishes surface radiance through bounded, fair page updates on the render thread.</summary>
internal sealed partial class LumonSceneRelightUpdateRenderer : IRenderer, ISurfaceLightingProvider, IDisposable
{
    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly LumonSceneFeedbackUpdateRenderer feedback;
    private readonly TraceGeometryRenderer geometry;
    private readonly LumonSceneRelightBatchSchedule batches = new();
    private readonly Dictionary<uint, ulong> identities = new();
    // Full seed completion is separate from publication of some initialized texels.
    private readonly HashSet<uint> seeded = new();
    private readonly HashSet<uint> initialized = new();
    private readonly HashSet<uint> publishedPages = new();
    private readonly Dictionary<uint, uint> traceBuckets = new();
    private readonly HashSet<uint> retrySeedNext = new();
    // Render-thread staging remains private; upload APIs only borrow spans during synchronous copies.
    private readonly List<LumonSceneRelightWorkGpu> seedWork = new();
    private readonly List<LumonSceneRelightWorkGpu> traceWork = new();
    private readonly List<LumonSceneRelightWorkGpu> resetWork = new();
    private readonly List<LumonSceneRelightWorkGpu> publishable = new();
    private readonly List<LumonSceneRelightWorkGpu> committedWork = new();
    private SurfaceLightingDispatch? dispatch;
    private GpuShaderStorageBuffer? readyBuffer;
    private uint[] readiness = Array.Empty<uint>();
    private uint[] candidates = Array.Empty<uint>();
    private LumonScenePhysicalAtlasGpuResources? atlas;
    private TraceGeometryGpuScene? scene;
    private long sceneRevision = -1, historyRevision = -1;
    private int settingsHash, cursor, frame, lastWorkCount;
    private SurfaceLightingSnapshot snapshot;
    private bool published;
    private static long nextDependencyRevision;
    private long dependencyRevision;
    public double RenderOrder => 0.99986;
    public int RenderRange => 1;

    #region Runtime lifecycle
    /// <summary>Registers the producer after capture and before subsequent frame consumers.</summary>
    public LumonSceneRelightUpdateRenderer(ICoreClientAPI capi, VgeConfig config,
        LumonSceneFeedbackUpdateRenderer feedback, TraceGeometryRenderer occupancy)
    {
        this.capi = capi; this.config = config; this.feedback = feedback; geometry = occupancy;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "vge_lumonscene_relight");
        capi.Event.LeaveWorld += OnLeaveWorld;
    }

    /// <summary>Returns only a generation whose geometry, settings and capture history still match its producer.</summary>
    public bool TryGetSurfaceLighting(out SurfaceLightingSnapshot result)
    {
        result = default;
        if (!config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled ||
            !feedback.TryGetNearDispatchState(out var pool, out _, out var mapping, out var mirror) ||
            !ReferenceEquals(pool.GpuResources, atlas) || readyBuffer == null)
            return false;
        var current = geometry.PrepareScene();
        if (!ReferenceEquals(current, scene) || current?.InvalidationRevision != sceneRevision ||
            feedback.GeometryHistoryRevision != historyRevision || SettingsHash() != settingsHash) return false;
        SynchronizePages(mapping, mirror, selectCandidates: false);
        if (!published) return false;
        snapshot = snapshot with { Origin = LumonSceneChunkSlotUniformState.OriginMinChunk,
            Dimensions = LumonSceneChunkSlotUniformState.Dims, Ring = LumonSceneChunkSlotUniformState.Ring };
        result = snapshot;
        return true;
    }

    /// <summary>Runs bounded batches and publishes initialized texels without waiting for unrelated lighting retries.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Done || !config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled) return;
        lastWorkCount = 0;
        if (!feedback.TryGetNearDispatchState(out var pool, out var gpu, out var mapping, out var mirror))
        { published = false; ReportReadiness("feedback-unavailable"); return; }
        if (pool.GpuResources is not { } resources || feedback.LightingSlots is not { } slots)
        { published = false; ReportReadiness("atlas-or-slots-unavailable"); return; }
        if (geometry.PrepareScene() is not { } current)
        { published = false; ReportReadiness("geometry-unavailable"); return; }
        var cfg = config.LumOn.LumonScene;
        int tile = pool.Plan.TileSizeTexels;
        int texels = Math.Min(tile * tile, cfg.RelightTexelsPerPagePerFrame);
        // Combining and carry-forward each have a separate fixed 64K-texel ceiling.
        int maxPages = Math.Min(cfg.RelightMaxPagesPerFrame, 65536 / (tile * tile));
        if (texels <= 0 || maxPages <= 0) { ReportReadiness("zero-budget"); return; }
        int hash = SettingsHash();
        bool changed = !ReferenceEquals(atlas, resources) || !ReferenceEquals(scene, current) ||
            sceneRevision != current.InvalidationRevision || historyRevision != feedback.GeometryHistoryRevision ||
            settingsHash != hash;
        if (changed)
        {
            diagnosticResets++;
            dependencyRevision = System.Threading.Interlocked.Increment(ref nextDependencyRevision);
            published = false; seeded.Clear(); initialized.Clear(); publishedPages.Clear(); traceBuckets.Clear(); retrySeedNext.Clear(); batches.Clear(); identities.Clear(); pageGenerations.Clear(); cursor = 0;
            atlas = resources; scene = current; sceneRevision = current.InvalidationRevision;
            historyRevision = feedback.GeometryHistoryRevision; settingsHash = hash;
            readiness = new uint[pool.Plan.CapacityPages + 1];
            readyBuffer ??= GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: "SurfaceLighting.Readiness");
            readyBuffer.EnsureCapacity(readiness.Length * sizeof(uint), growExponentially: false);
            readyBuffer.UploadSubData<uint>(readiness, 0, readiness.Length * sizeof(uint));
        }
        if (readyBuffer == null) return;
        SynchronizePages(mapping, mirror, selectCandidates: true);
        if (candidates.Length == 0) { ReportReadiness(mapping.Count == 0 ? "no-resident-pages" : "awaiting-capture"); return; }
        dispatch ??= new SurfaceLightingDispatch(capi);
        snapshot = new(resources.PublishedOutgoing, resources.DirectAtlas, resources.IrradianceAtlas,
            gpu.PageTable.PageTableMip0, resources.MaterialAtlas, gpu.PatchMetadata.Ssbo, slots, readyBuffer,
            LumonSceneChunkSlotUniformState.OriginMinChunk, LumonSceneChunkSlotUniformState.Dims,
            LumonSceneChunkSlotUniformState.Ring, tile, pool.Plan.TilesPerAxis, pool.Plan.TilesPerAtlas, resources.LightingGeneration, dependencyRevision);

        int count = Math.Min(maxPages, candidates.Length), batchCount = (tile * tile + texels - 1) / texels;
        publishable.Clear();
        lastWorkCount = 0;
        seedWork.Clear();
        traceWork.Clear();
        resetWork.Clear();
        for (int i = 0; i < count; i++)
        {
            uint id = candidates[cursor++ % candidates.Length]; ulong key = identities[id];
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key), page = LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(mirror[checked((int)slot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk + (int)page)]);
            if ((flags & (LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.Capturing)) != 0) continue;
            uint bucket;
            bool trace = seeded.Contains(id) || (publishedPages.Contains(id) && !retrySeedNext.Contains(id));
            if (trace)
            {
                if (!seeded.Contains(id)) retrySeedNext.Add(id);
                traceBuckets.TryGetValue(id, out bucket);
                traceBuckets[id] = (bucket + 1) % (uint)batchCount;
            }
            else
            {
                retrySeedNext.Remove(id);
                bucket = batches.Next(key, batchCount);
            }
            var work = new LumonSceneRelightWorkGpu(id, slot, bucket, page);
            if (!initialized.Contains(id)) resetWork.Add(work);
            (trace ? traceWork : seedWork).Add(work);
        }
        if (resetWork.Count > 0)
        {
            // New identities must never inherit direct validity or indirect weight from reused storage.
            // Reset cost shares the same page admission and fixed 64K-texel ceiling as combine/copy.
            gpu.RelightWork.ResetAndUpload(CollectionsMarshal.AsSpan(resetWork));
            dispatch.Run(current, snapshot, resources.PendingOutgoing, gpu.RelightWork.Items, resetWork.Count, 3u,
                (uint)(tile * tile), 1, 0, (uint)frame, cfg.SurfaceLightingMaterialEmission);
            foreach (var item in resetWork) initialized.Add(item.PhysicalPageId);
        }
        for (uint operation = 0; operation < 2; operation++)
        {
            var work = CollectionsMarshal.AsSpan(operation == 0 ? seedWork : traceWork);
            if (work.Length == 0) continue;
            gpu.RelightWork.ResetAndUpload(work);
            dispatch.Run(current, snapshot, resources.PendingOutgoing, gpu.RelightWork.Items, work.Length, operation,
                (uint)texels, (uint)Math.Max(1,cfg.RelightRaysPerTexel), (uint)cfg.RelightMaxDdaSteps, (uint)frame, cfg.SurfaceLightingMaterialEmission);
            // One completion read per operation, rather than a GPU synchronization for every page.
            using var result = gpu.RelightWork.Items.MapRange<LumonSceneRelightWorkGpu>(0, work.Length, MapBufferAccessMask.MapReadBit);
            for (int i = 0; i < work.Length; i++)
            {
                bool success = result.IsMapped && (result.Span[i].VirtualPageIndex & 0x80000000u) == 0;
                if (!result.IsMapped) diagnosticReadbackFailures++;
                if (operation == 0) { diagnosticSeedAttempts++; if (!success) diagnosticSeedFailures++; }
                else { diagnosticIndirectAttempts++; if (!success) diagnosticIndirectFailures++; }
                // A successful texel is useful even when another texel in this batch remains unresolved.
                bool progress = result.IsMapped && (result.Span[i].VirtualPageIndex & 0x40000000u) != 0;
                bool seedComplete = operation == 0 && batches.Complete(identities[work[i].PhysicalPageId], batchCount, success);
                if (seedComplete) seeded.Add(work[i].PhysicalPageId);
                if (progress || seedComplete) publishable.Add(work[i]);
            }
            lastWorkCount += work.Length;
        }
        cursor %= candidates.Length;
        if (publishable.Count > 0)
        {
            var work = CollectionsMarshal.AsSpan(publishable);
            gpu.RelightWork.ResetAndUpload(work);
            dispatch.Run(current, snapshot, resources.PendingOutgoing, gpu.RelightWork.Items, work.Length, 2u,
                (uint)(tile*tile), 1, 0, (uint)frame, cfg.SurfaceLightingMaterialEmission);
            // Keep the submitted list unchanged while its span is borrowed for completion handling.
            committedWork.Clear();
            using (var result = gpu.RelightWork.Items.MapRange<LumonSceneRelightWorkGpu>(0, work.Length, MapBufferAccessMask.MapReadBit))
            {
                for (int i = 0; i < work.Length; i++)
                {
                    if (result.IsMapped && (result.Span[i].VirtualPageIndex & 0x40000000u) != 0)
                        committedWork.Add(work[i]);
                    else
                    {
                        diagnosticCombineFailures++;
                        if (!result.IsMapped) diagnosticReadbackFailures++;
                        // Failed readback or an entirely unavailable output cannot be committed. Restore the tile
                        // so swapping another successful tile never exposes an incomplete generation.
                        CopyOutgoingTile(resources.PublishedOutgoing, resources.PendingOutgoing, work[i].PhysicalPageId, pool.Plan);
                    }
                }
            }
            if (committedWork.Count == 0) { ReportReadiness("combine-rejected"); frame++; return; }
            resources.Publish();
            // The previous destination already mirrors unchanged tiles. Only changed, fully initialized output tiles
            // need copying back after the swap; GL command ordering protects submitted readers.
            foreach (var item in committedWork)
            {
                CopyOutgoingTile(resources.PublishedOutgoing, resources.PendingOutgoing, item.PhysicalPageId, pool.Plan);
                publishedPages.Add(item.PhysicalPageId);
                readiness[item.PhysicalPageId] = 1;
                readyBuffer.UploadSubData<uint>(readiness.AsSpan((int)item.PhysicalPageId, 1), checked((int)((long)item.PhysicalPageId << 2)), 4);
                if (seeded.Contains(item.PhysicalPageId))
                    feedback.TryClearNearPageFlagsMip0(item.ChunkSlot, (int)item.VirtualPageIndex, LumonScenePageTableEntryPacking.Flags.NeedsRelight);
            }
            GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit);
            snapshot = snapshot with { OutgoingRadiance = resources.PublishedOutgoing, Generation = resources.LightingGeneration };
            published = true;
        }
        ReportReadiness(published ? "published" : "seeding");
        frame++;
    }

    /// <summary>Copies one complete page between outgoing generations without touching neighboring tiles.</summary>
    private static void CopyOutgoingTile(Texture3D source, Texture3D destination, uint page, LumonScenePhysicalPoolPlan plan)
    {
        uint index = page - 1;
        int layer = (int)(index / (uint)plan.TilesPerAtlas);
        int local = (int)(index % (uint)plan.TilesPerAtlas);
        int x = local % plan.TilesPerAxis * plan.TileSizeTexels;
        int y = local / plan.TilesPerAxis * plan.TileSizeTexels;
        GL.CopyImageSubData(source.TextureId, ImageTarget.Texture2DArray, 0, x, y, layer,
            destination.TextureId, ImageTarget.Texture2DArray, 0, x, y, layer, plan.TileSizeTexels, plan.TileSizeTexels, 1);
    }

    /// <summary>Includes all sampling and source-policy changes that alter the meaning of temporal batches.</summary>
    private int SettingsHash()
    {
        var cfg = config.LumOn.LumonScene;
        return HashCode.Combine(cfg.RelightRaysPerTexel, cfg.RelightMaxDdaSteps, cfg.RelightTexelsPerPagePerFrame, cfg.SurfaceLightingMaterialEmission);
    }

    /// <summary>Reports bounded producer progress without reading GPU counters.</summary>
    internal bool TryGetSelfCheckLine(out string line)
    {
        line = ReadinessDiagnosticLine();
        return config.LumOn.Enabled && config.LumOn.LumonScene.Enabled;
    }

    /// <summary>Retires borrowed publication references before disposing private GPU resources.</summary>
    private void OnLeaveWorld()
    {
        published = false; snapshot = default; atlas = null; scene = null;
        identities.Clear(); pageGenerations.Clear(); seeded.Clear(); initialized.Clear(); publishedPages.Clear(); traceBuckets.Clear(); retrySeedNext.Clear(); batches.Clear(); readiness = Array.Empty<uint>();
        readyBuffer?.Dispose(); readyBuffer = null; dispatch?.Dispose(); dispatch = null;
        seedWork.Clear(); traceWork.Clear(); resetWork.Clear(); publishable.Clear(); committedWork.Clear();
        cursor = frame = lastWorkCount = 0; sceneRevision = historyRevision = -1;
        ResetReadinessDiagnostics();
    }

    /// <summary>Unregisters callbacks and releases producer-owned resources.</summary>
    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Done);
        capi.Event.LeaveWorld -= OnLeaveWorld; OnLeaveWorld();
    }
    #endregion
}
