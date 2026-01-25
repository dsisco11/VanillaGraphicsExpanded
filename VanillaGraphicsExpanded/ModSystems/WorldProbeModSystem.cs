using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class WorldProbeModSystem : ModSystem, ILiveConfigurable
{
    private const int MaxPendingWorldProbeDirtyChunks = 2048;

    private ICoreClientAPI? capi;
    private IEventAPI? commonEvents;

    private LumOnWorldProbeClipmapBufferManager? clipmapBufferManager;

    private VanillaGraphicsExpanded.LumOn.WorldProbes.LumOnWorldProbeUpdateRenderer? worldProbeUpdateRenderer;

    private WorldProbeLiveConfigSnapshot? lastSnapshot;

    private readonly object pendingWorldProbeDirtyChunkLock = new();
    private readonly HashSet<ulong> pendingWorldProbeDirtyChunkKeys = new();
    private int pendingWorldProbeDirtyChunkOverflow;

    private readonly record struct WorldProbeLiveConfigSnapshot(
        bool LumOnEnabled,
        float ClipmapBaseSpacing,
        int ClipmapResolution,
        int ClipmapLevels,
        int ClipmapBudgetsHash,
        int ClipmapTraceMaxProbesPerFrame,
        int ClipmapUploadBudgetBytesPerFrame)
    {
        public static WorldProbeLiveConfigSnapshot From(VgeConfig cfg)
        {
            var c = cfg.WorldProbeClipmap;
            return new WorldProbeLiveConfigSnapshot(
                cfg.LumOn.Enabled,
                c.ClipmapBaseSpacing,
                c.ClipmapResolution,
                c.ClipmapLevels,
                HashBudgets(c.PerLevelProbeUpdateBudget),
                c.TraceMaxProbesPerFrame,
                c.UploadBudgetBytesPerFrame);
        }

        private static int HashBudgets(int[]? budgets)
        {
            if (budgets is null) return 0;

            int hash = HashCode.Combine(budgets.Length);
            foreach (int b in budgets)
            {
                hash = HashCode.Combine(hash, b);
            }

            return hash;
        }
    }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        commonEvents = ((ICoreAPI)api).Event;

        // Track world changes so the world-probe clipmap can re-trace affected regions.
        api.Event.BlockChanged += OnClientBlockChanged;
        commonEvents.ChunkDirty += OnChunkDirty;

        // Snapshot initial config state so live reload deltas are correct.
        ConfigModSystem.Config.Sanitize();
        lastSnapshot = WorldProbeLiveConfigSnapshot.From(ConfigModSystem.Config);

        // If LumOn starts enabled, initialize clipmap resources immediately so the renderer can run Phase 18.
        if (ConfigModSystem.Config.LumOn.Enabled)
        {
            var clipmap = EnsureClipmapResources(api, "startup");
            worldProbeUpdateRenderer ??= new VanillaGraphicsExpanded.LumOn.WorldProbes.LumOnWorldProbeUpdateRenderer(
                api,
                ConfigModSystem.Config,
                clipmap);
        }
    }

    internal LumOnWorldProbeClipmapBufferManager? GetClipmapBufferManagerOrNull()
    {
        return clipmapBufferManager;
    }

    internal LumOnWorldProbeClipmapBufferManager EnsureClipmapResources(ICoreClientAPI api, string reason)
    {
        capi ??= api;

        clipmapBufferManager ??= new LumOnWorldProbeClipmapBufferManager(capi, ConfigModSystem.Config);
        clipmapBufferManager.EnsureResources();
        capi.Logger.Debug("[VGE] World-probe clipmap resources ensured: {0}", reason);
        return clipmapBufferManager;
    }

    internal void NotifyWorldProbeChunkDirty(int chunkX, int chunkY, int chunkZ, string reason)
    {
        // Buffering only; the world-probe update renderer will drain + apply to the scheduler.
        if (!ConfigModSystem.Config.LumOn.Enabled)
        {
            return;
        }

        ulong key = EncodeChunkKey(chunkX, chunkY, chunkZ);

        lock (pendingWorldProbeDirtyChunkLock)
        {
            if (pendingWorldProbeDirtyChunkKeys.Count < MaxPendingWorldProbeDirtyChunks)
            {
                pendingWorldProbeDirtyChunkKeys.Add(key);
            }
            else
            {
                pendingWorldProbeDirtyChunkOverflow++;
            }
        }
    }

    internal void DrainPendingWorldProbeDirtyChunks(Action<int, int, int> onChunk, out int overflowCount)
    {
        ulong[] keysToDrain;
        int overflow;

        lock (pendingWorldProbeDirtyChunkLock)
        {
            if (pendingWorldProbeDirtyChunkKeys.Count == 0 && pendingWorldProbeDirtyChunkOverflow == 0)
            {
                overflowCount = 0;
                return;
            }

            keysToDrain = new ulong[pendingWorldProbeDirtyChunkKeys.Count];
            pendingWorldProbeDirtyChunkKeys.CopyTo(keysToDrain);
            pendingWorldProbeDirtyChunkKeys.Clear();

            overflow = pendingWorldProbeDirtyChunkOverflow;
            pendingWorldProbeDirtyChunkOverflow = 0;
        }

        foreach (ulong key in keysToDrain)
        {
            DecodeChunkKey(key, out int cx, out int cy, out int cz);
            onChunk(cx, cy, cz);
        }

        overflowCount = overflow;
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI clientApi) return;

        ConfigModSystem.Config.Sanitize();

        var current = WorldProbeLiveConfigSnapshot.From(ConfigModSystem.Config);
        if (lastSnapshot is null)
        {
            lastSnapshot = current;
            return;
        }

        var prev = lastSnapshot.Value;
        lastSnapshot = current;

        // Only engage world-probe resources when LumOn is enabled.
        if (!current.LumOnEnabled)
        {
            return;
        }

        // Ensure the update renderer exists when LumOn is enabled.
        if (worldProbeUpdateRenderer is null)
        {
            var clipmap = clipmapBufferManager ?? EnsureClipmapResources(clientApi, "live config reload");
            worldProbeUpdateRenderer = new VanillaGraphicsExpanded.LumOn.WorldProbes.LumOnWorldProbeUpdateRenderer(
                clientApi,
                ConfigModSystem.Config,
                clipmap);
        }

        // Phase 18 world-probe config: classify hot-reload behavior.
        // - Budgets are safe live updates.
        // - Topology changes (spacing/resolution/levels) require resource reallocation + invalidation.
        bool clipmapTopologyChanged =
            prev.ClipmapBaseSpacing != current.ClipmapBaseSpacing ||
            prev.ClipmapResolution != current.ClipmapResolution ||
            prev.ClipmapLevels != current.ClipmapLevels;

        bool clipmapBudgetsChanged =
            prev.ClipmapBudgetsHash != current.ClipmapBudgetsHash ||
            prev.ClipmapTraceMaxProbesPerFrame != current.ClipmapTraceMaxProbesPerFrame ||
            prev.ClipmapUploadBudgetBytesPerFrame != current.ClipmapUploadBudgetBytesPerFrame;

        if (clipmapTopologyChanged)
        {
            if (clipmapBufferManager is not null)
            {
                clipmapBufferManager.RequestRecreate("live config change (clipmap topology)");
                clipmapBufferManager.EnsureResources();
                clientApi.Logger.Notification("[VGE] World-probe clipmap topology changed (spacing/resolution/levels). Resources recreated.");
            }
            else
            {
                clientApi.Logger.Notification("[VGE] World-probe clipmap topology changed (spacing/resolution/levels). Will apply once Phase 18 clipmap resources exist; re-entering the world may be required.");
            }
        }
        else if (clipmapBudgetsChanged)
        {
            clientApi.Logger.Debug("[VGE] World-probe clipmap budgets updated (live).");
        }

        // Ensure resources exist when LumOn is enabled, even if no deltas were observed.
        if (clipmapBufferManager is null)
        {
            EnsureClipmapResources(clientApi, "live config reload");
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        if (capi is not null)
        {
            capi.Event.BlockChanged -= OnClientBlockChanged;
        }

        if (commonEvents is not null)
        {
            commonEvents.ChunkDirty -= OnChunkDirty;
            commonEvents = null;
        }

        worldProbeUpdateRenderer?.Dispose();
        worldProbeUpdateRenderer = null;

        clipmapBufferManager?.Dispose();
        clipmapBufferManager = null;
        capi = null;
        lastSnapshot = null;
    }

    private void OnClientBlockChanged(BlockPos pos, Block oldBlock)
    {
        // Coalesce at chunk granularity; the scheduler invalidation works on probe centers.
        // Right shift works for negatives because chunk size is a power-of-two.
        int shift = 5; // log2(GlobalConstants.ChunkSize) (32)
        int cx = pos.X >> shift;
        int cy = pos.Y >> shift;
        int cz = pos.Z >> shift;

        NotifyWorldProbeChunkDirty(cx, cy, cz, reason: "BlockChanged");
    }

    private void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        // NewlyLoaded is effectively "chunk loaded"; MarkedDirty covers late-arriving edits.
        if (reason != EnumChunkDirtyReason.NewlyLoaded &&
            reason != EnumChunkDirtyReason.MarkedDirty &&
            reason != EnumChunkDirtyReason.NewlyCreated)
        {
            return;
        }

        NotifyWorldProbeChunkDirty(chunkCoord.X, chunkCoord.Y, chunkCoord.Z, reason: $"ChunkDirty:{reason}");
    }

    private static ulong EncodeChunkKey(int chunkX, int chunkY, int chunkZ)
    {
        const ulong mask = (1ul << 21) - 1ul;

        static ulong ZigZag(int v) => (ulong)((v << 1) ^ (v >> 31));

        ulong x = ZigZag(chunkX) & mask;
        ulong y = ZigZag(chunkY) & mask;
        ulong z = ZigZag(chunkZ) & mask;

        return x | (y << 21) | (z << 42);
    }

    private static int DecodeZigZag(ulong v)
    {
        // (v >> 1) ^ -(v & 1)
        return (int)((v >> 1) ^ (ulong)-(long)(v & 1ul));
    }

    private static void DecodeChunkKey(ulong key, out int chunkX, out int chunkY, out int chunkZ)
    {
        const ulong mask = (1ul << 21) - 1ul;
        chunkX = DecodeZigZag(key & mask);
        chunkY = DecodeZigZag((key >> 21) & mask);
        chunkZ = DecodeZigZag((key >> 42) & mask);
    }
}
