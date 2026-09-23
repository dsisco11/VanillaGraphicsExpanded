using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Produces and publishes surface radiance through bounded, fair page updates on the render thread.</summary>
internal sealed class LumonSceneRelightUpdateRenderer : IRenderer, ISurfaceLightingProvider, IDisposable
{
    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly LumonSceneFeedbackUpdateRenderer feedback;
    private readonly TraceGeometryRenderer geometry;
    private readonly LumonSceneRelightBatchSchedule batches = new();
    private readonly Dictionary<uint, ulong> identities = new();
    private readonly HashSet<uint> seeded = new();
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
        if (!published || !config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled ||
            !feedback.TryGetNearDispatchState(out var pool, out _, out var mapping, out var mirror) ||
            !ReferenceEquals(pool.GpuResources, atlas) || mapping.Count != identities.Count ||
            mapping.Any(pair => !identities.TryGetValue(pair.Key, out ulong key) || key != pair.Value))
            return false;
        foreach (uint id in seeded)
        {
            ulong key = identities[id];
            int index = checked((int)LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key) * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk +
                (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key));
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(mirror[index]);
            if ((flags & (LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.Capturing)) != 0) return false;
        }
        var current = geometry.PrepareScene();
        if (!ReferenceEquals(current, scene) || current?.InvalidationRevision != sceneRevision ||
            feedback.GeometryHistoryRevision != historyRevision || SettingsHash() != settingsHash) return false;
        result = snapshot;
        return true;
    }

    /// <summary>Runs bounded seed or indirect batches and publishes only pages with complete successful sweeps.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Done || !config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled) return;
        if (!feedback.TryGetNearDispatchState(out var pool, out var gpu, out var mapping, out var mirror) ||
            pool.GpuResources is not { } resources || feedback.LightingSlots is not { } slots ||
            geometry.PrepareScene() is not { } current) { published = false; return; }
        var cfg = config.LumOn.LumonScene;
        int tile = pool.Plan.TileSizeTexels;
        int texels = Math.Min(tile * tile, cfg.RelightTexelsPerPagePerFrame);
        // Combining and carry-forward each have a separate fixed 64K-texel ceiling.
        int maxPages = Math.Min(cfg.RelightMaxPagesPerFrame, 65536 / (tile * tile));
        if (texels <= 0 || maxPages <= 0) return;
        int hash = SettingsHash();
        bool changed = !ReferenceEquals(atlas, resources) || !ReferenceEquals(scene, current) ||
            sceneRevision != current.InvalidationRevision || historyRevision != feedback.GeometryHistoryRevision ||
            settingsHash != hash || mapping.Count != identities.Count ||
            mapping.Any(pair => !identities.TryGetValue(pair.Key, out ulong key) || key != pair.Value);
        if (changed)
        {
            published = false; seeded.Clear(); batches.Clear(); identities.Clear(); cursor = 0;
            foreach (var pair in mapping) identities.Add(pair.Key, pair.Value);
            candidates = identities.Keys.OrderBy(id => id).ToArray();
            atlas = resources; scene = current; sceneRevision = current.InvalidationRevision;
            historyRevision = feedback.GeometryHistoryRevision; settingsHash = hash;
            readiness = new uint[pool.Plan.CapacityPages + 1];
            readyBuffer ??= GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: "SurfaceLighting.Readiness");
            readyBuffer.EnsureCapacity(readiness.Length * sizeof(uint), growExponentially: false);
            readyBuffer.UploadSubData<uint>(readiness, 0, readiness.Length * sizeof(uint));
        }
        if (readyBuffer == null || identities.Count == 0) return;
        dispatch ??= new SurfaceLightingDispatch(capi);
        snapshot = new(resources.PublishedOutgoing, resources.DirectAtlas, resources.IrradianceAtlas,
            gpu.PageTable.PageTableMip0, resources.MaterialAtlas, gpu.PatchMetadata.Ssbo, slots, readyBuffer,
            LumonSceneChunkSlotUniformState.OriginMinChunk, LumonSceneChunkSlotUniformState.Dims,
            LumonSceneChunkSlotUniformState.Ring, tile, pool.Plan.TilesPerAxis, pool.Plan.TilesPerAtlas, resources.LightingGeneration);

        int count = Math.Min(maxPages, candidates.Length), batchCount = (tile * tile + texels - 1) / texels;
        var complete = new List<LumonSceneRelightWorkGpu>(count);
        lastWorkCount = 0;
        var seedWork = new List<LumonSceneRelightWorkGpu>(count);
        var traceWork = new List<LumonSceneRelightWorkGpu>(count);
        for (int i = 0; i < count; i++)
        {
            uint id = candidates[cursor++ % candidates.Length]; ulong key = identities[id];
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key), page = LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(mirror[checked((int)slot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk + (int)page)]);
            if ((flags & (LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.Capturing)) != 0) continue;
            var work = new LumonSceneRelightWorkGpu(id, slot, batches.Next(key, batchCount), page);
            (seeded.Contains(id) ? traceWork : seedWork).Add(work);
        }
        for (uint operation = 0; operation < 2; operation++)
        {
            var work = (operation == 0 ? seedWork : traceWork).ToArray();
            if (work.Length == 0) continue;
            gpu.RelightWork.ResetAndUpload(work.AsSpan());
            dispatch.Run(current, snapshot, resources.PendingOutgoing, gpu.RelightWork.Items, work.Length, operation,
                (uint)texels, (uint)Math.Max(1,cfg.RelightRaysPerTexel), (uint)cfg.RelightMaxDdaSteps, (uint)frame, cfg.SurfaceLightingMaterialEmission);
            // One completion read per operation, rather than a GPU synchronization for every page.
            using var result = gpu.RelightWork.Items.MapRange<LumonSceneRelightWorkGpu>(0, work.Length, MapBufferAccessMask.MapReadBit);
            for (int i = 0; i < work.Length; i++)
            {
                bool success = result.IsMapped && (result.Span[i].VirtualPageIndex & 0x80000000u) == 0;
                if (batches.Complete(identities[work[i].PhysicalPageId], batchCount, success)) complete.Add(work[i]);
            }
            lastWorkCount += work.Length;
        }
        cursor %= candidates.Length;
        if (complete.Count > 0)
        {
            var work = complete.ToArray();
            gpu.RelightWork.ResetAndUpload(work.AsSpan());
            dispatch.Run(current, snapshot, resources.PendingOutgoing, gpu.RelightWork.Items, work.Length, 2u,
                (uint)(tile*tile), 1, 0, (uint)frame, cfg.SurfaceLightingMaterialEmission);
            resources.Publish();
            // The previous destination already mirrors unchanged tiles. Only changed complete tiles
            // need copying back after the swap; GL command ordering protects submitted readers.
            foreach (var item in work)
            {
                uint index = item.PhysicalPageId - 1;
                int layer = (int)(index / (uint)pool.Plan.TilesPerAtlas);
                int local = (int)(index % (uint)pool.Plan.TilesPerAtlas);
                int x = local % pool.Plan.TilesPerAxis * tile, y = local / pool.Plan.TilesPerAxis * tile;
                GL.CopyImageSubData(resources.PublishedOutgoing.TextureId, ImageTarget.Texture2DArray, 0, x, y, layer,
                    resources.PendingOutgoing.TextureId, ImageTarget.Texture2DArray, 0, x, y, layer, tile, tile, 1);
                seeded.Add(item.PhysicalPageId);
                readiness[item.PhysicalPageId] = 1;
                readyBuffer.UploadSubData<uint>(readiness.AsSpan((int)item.PhysicalPageId, 1), checked((int)item.PhysicalPageId * 4), 4);
                feedback.TryClearNearPageFlagsMip0(item.ChunkSlot, (int)item.VirtualPageIndex, LumonScenePageTableEntryPacking.Flags.NeedsRelight);
            }
            GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit);
            snapshot = snapshot with { OutgoingRadiance = resources.PublishedOutgoing, Generation = resources.LightingGeneration };
            published = true;
        }
        frame++;
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
        line = $"LSR: pages:{lastWorkCount} ready:{seeded.Count} generation:{snapshot.Generation}";
        return config.LumOn.Enabled && config.LumOn.LumonScene.Enabled;
    }

    /// <summary>Retires borrowed publication references before disposing private GPU resources.</summary>
    private void OnLeaveWorld()
    {
        published = false; snapshot = default; atlas = null; scene = null;
        identities.Clear(); seeded.Clear(); batches.Clear(); readiness = Array.Empty<uint>();
        readyBuffer?.Dispose(); readyBuffer = null; dispatch?.Dispose(); dispatch = null;
        cursor = frame = 0; sceneRevision = historyRevision = -1;
    }

    /// <summary>Unregisters callbacks and releases producer-owned resources.</summary>
    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Done);
        capi.Event.LeaveWorld -= OnLeaveWorld; OnLeaveWorld();
    }
    #endregion
}
