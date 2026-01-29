using System;

using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Diagnostics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class LumOnModSystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? capi;
    private IEventAPI? commonEvents;

    private GBufferManager? gBufferManager;
    private DirectLightingBufferManager? directLightingBufferManager;

    private LumOnBufferManager? lumOnBufferManager;
    private LumOnRenderer? lumOnRenderer;
    private LumOnDebugRenderer? lumOnDebugRenderer;
    private LumonSceneFeedbackUpdateRenderer? lumonSceneFeedbackUpdateRenderer;
    private LumonSceneOccupancyClipmapUpdateRenderer? lumonSceneOccupancyClipmapUpdateRenderer;

    private HudLumOnStatsPanel? lumOnStatsPanel;

    private LumOnLiveConfigSnapshot? lastLiveConfigSnapshot;

    private readonly record struct LumOnLiveConfigSnapshot(
        bool LumOnEnabled,
        int ProbeSpacingPx,
        bool HalfResolution)
    {
        public static LumOnLiveConfigSnapshot From(VgeConfig cfg)
        {
            var c = cfg.LumOn;
            return new LumOnLiveConfigSnapshot(
                c.Enabled,
                c.ProbeSpacingPx,
                c.HalfResolution);
        }
    }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        commonEvents = ((ICoreAPI)api).Event;

        api.Event.BlockChanged += OnClientBlockChanged;
        commonEvents.ChunkDirty += OnChunkDirty;

        ConfigModSystem.Config.Sanitize();
        lastLiveConfigSnapshot = LumOnLiveConfigSnapshot.From(ConfigModSystem.Config);

        EnsureInitializedIfReady("startup");

        EnsureLumOnStatsPanelInitialized("startup");
    }

    internal bool IsLumOnEnabled()
        => ConfigModSystem.Config.LumOn.Enabled;

    internal void ToggleLumOnEnabled()
    {
        if (capi is null)
        {
            return;
        }

        ConfigModSystem.Config.LumOn.Enabled = !ConfigModSystem.Config.LumOn.Enabled;
        string status = ConfigModSystem.Config.LumOn.Enabled ? "enabled" : "disabled";
        capi.TriggerIngameError(this, "vgelumon", $"[LumOn] {status}");

        if (ConfigModSystem.Config.LumOn.Enabled)
        {
            EnsureInitializedIfReady("tools toggle enable");
            TryBindWorldProbeClipmap(capi, reason: "tools toggle enable");
        }
    }

    internal bool IsLumOnStatsOverlayShown()
        => lumOnStatsPanel?.IsOpened() ?? false;

    internal void ToggleLumOnStatsOverlay()
    {
        if (capi is null)
        {
            return;
        }

        EnsureLumOnStatsPanelInitialized("toggle");
        if (lumOnStatsPanel is null)
        {
            return;
        }

        if (lumOnStatsPanel.IsOpened())
        {
            lumOnStatsPanel.Hide();
        }
        else
        {
            lumOnStatsPanel.Show();
        }
    }

    internal string[] GetLumOnStatsOverlayLines()
    {
        if (!ConfigModSystem.Config.LumOn.Enabled)
        {
            return ["LumOn: Disabled", string.Empty, string.Empty, string.Empty];
        }

        if (lumOnRenderer is null)
        {
            return ["LumOn: Enabled (not initialized)", "Waiting for renderer dependencies...", string.Empty, string.Empty];
        }

        return lumOnRenderer.DebugCounters.GetDebugLines();
    }

    internal LumOnBufferManager? GetLumOnBufferManagerOrNull()
    {
        return lumOnBufferManager;
    }

    internal void SetDependencies(
        ICoreClientAPI api,
        GBufferManager gBufferManager,
        DirectLightingBufferManager directLightingBufferManager)
    {
        capi ??= api;
        this.gBufferManager = gBufferManager;
        this.directLightingBufferManager = directLightingBufferManager;

        EnsureInitializedIfReady("dependencies ready");
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI clientApi)
        {
            return;
        }

        ConfigModSystem.Config.Sanitize();

        var current = LumOnLiveConfigSnapshot.From(ConfigModSystem.Config);
        if (lastLiveConfigSnapshot is null)
        {
            lastLiveConfigSnapshot = current;
            return;
        }

        var prev = lastLiveConfigSnapshot.Value;
        lastLiveConfigSnapshot = current;

        // LumOn enable: create missing runtime objects.
        // LumOn disable: keep objects alive (renderer remains registered) but it will early-out.
        if (current.LumOnEnabled && lumOnRenderer is null)
        {
            EnsureInitializedIfReady("live config enable");
        }

        // Screen-probe resource sizing depends on these keys.
        if (lumOnBufferManager is not null)
        {
            if (prev.ProbeSpacingPx != current.ProbeSpacingPx || prev.HalfResolution != current.HalfResolution)
            {
                lumOnBufferManager.RequestRecreateBuffers("live config change (ProbeSpacingPx/HalfResolution)");
            }
        }

        // Ensure Phase 18 clipmap resources exist and are bound when LumOn is enabled.
        if (current.LumOnEnabled)
        {
            TryBindWorldProbeClipmap(clientApi, reason: "live config reload");
        }

        // Phase 22: ensure LumonScene feedback renderer exists when enabled.
        if (current.LumOnEnabled
            && ConfigModSystem.Config.LumOn.LumonScene.Enabled
            && lumonSceneFeedbackUpdateRenderer is null
            && gBufferManager is not null)
        {
            lumonSceneFeedbackUpdateRenderer = new LumonSceneFeedbackUpdateRenderer(clientApi, ConfigModSystem.Config, gBufferManager);
        }

        if (current.LumOnEnabled
            && ConfigModSystem.Config.LumOn.LumonScene.Enabled
            && lumonSceneOccupancyClipmapUpdateRenderer is null)
        {
            lumonSceneOccupancyClipmapUpdateRenderer = new LumonSceneOccupancyClipmapUpdateRenderer(clientApi, ConfigModSystem.Config);
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

        lumOnStatsPanel?.Dispose();
        lumOnStatsPanel = null;

        lumOnDebugRenderer?.Dispose();
        lumOnDebugRenderer = null;

        lumonSceneFeedbackUpdateRenderer?.Dispose();
        lumonSceneFeedbackUpdateRenderer = null;

        lumonSceneOccupancyClipmapUpdateRenderer?.Dispose();
        lumonSceneOccupancyClipmapUpdateRenderer = null;

        lumOnRenderer?.Dispose();
        lumOnRenderer = null;

        lumOnBufferManager?.Dispose();
        lumOnBufferManager = null;

        gBufferManager = null;
        directLightingBufferManager = null;
        capi = null;
        lastLiveConfigSnapshot = null;
    }

    private void OnClientBlockChanged(Vintagestory.API.MathTools.BlockPos pos, Vintagestory.API.Common.Block oldBlock)
    {
        _ = pos;
        _ = oldBlock;

        if (!ConfigModSystem.Config.LumOn.Enabled || !ConfigModSystem.Config.LumOn.LumonScene.Enabled)
        {
            return;
        }

        lumonSceneFeedbackUpdateRenderer?.NotifyAllDirty("BlockChanged");
        lumonSceneOccupancyClipmapUpdateRenderer?.NotifyAllDirty("BlockChanged");
    }

    private void OnChunkDirty(Vintagestory.API.MathTools.Vec3i chunkCoord, Vintagestory.API.Common.IWorldChunk chunk, Vintagestory.API.Common.EnumChunkDirtyReason reason)
    {
        _ = chunkCoord;
        _ = chunk;

        if (reason != Vintagestory.API.Common.EnumChunkDirtyReason.NewlyLoaded &&
            reason != Vintagestory.API.Common.EnumChunkDirtyReason.MarkedDirty &&
            reason != Vintagestory.API.Common.EnumChunkDirtyReason.NewlyCreated)
        {
            return;
        }

        if (!ConfigModSystem.Config.LumOn.Enabled || !ConfigModSystem.Config.LumOn.LumonScene.Enabled)
        {
            return;
        }

        lumonSceneFeedbackUpdateRenderer?.NotifyAllDirty($"ChunkDirty:{reason}");
        lumonSceneOccupancyClipmapUpdateRenderer?.NotifyAllDirty($"ChunkDirty:{reason}");
    }

    private void EnsureLumOnStatsPanelInitialized(string reason)
    {
        if (capi is null)
        {
            return;
        }

        lumOnStatsPanel ??= new HudLumOnStatsPanel(capi, this);
        capi.Logger.Debug("[VGE] LumOn stats panel ensured ({0})", reason);
    }

    private void EnsureInitializedIfReady(string reason)
    {
        if (capi is null)
        {
            return;
        }

        if (gBufferManager is null || directLightingBufferManager is null)
        {
            return;
        }

        // Initialize LumOn based on config (loaded by ConfigModSystem).
        if (!ConfigModSystem.Config.LumOn.Enabled)
        {
            return;
        }

        lumOnBufferManager ??= new LumOnBufferManager(capi, ConfigModSystem.Config);

        var clipmapManager = capi.ModLoader.GetModSystem<WorldProbeModSystem>().EnsureClipmapResources(capi, $"LumOn ensure ({reason})");

        if (lumOnRenderer is null)
        {
            capi.Logger.Notification("[VGE] LumOn enabled - using Screen Probe Gather");
            lumOnRenderer = new LumOnRenderer(capi, ConfigModSystem.Config, lumOnBufferManager, gBufferManager, clipmapManager);
        }
        else
        {
            lumOnRenderer.SetWorldProbeClipmapBufferManager(clipmapManager);
        }

        if (lumOnDebugRenderer is null)
        {
            lumOnDebugRenderer = new LumOnDebugRenderer(capi, ConfigModSystem.Config, lumOnBufferManager, gBufferManager, directLightingBufferManager, clipmapManager);
        }
        else
        {
            lumOnDebugRenderer.SetWorldProbeClipmapBufferManager(clipmapManager);
        }

        if (lumonSceneFeedbackUpdateRenderer is null && ConfigModSystem.Config.LumOn.LumonScene.Enabled)
        {
            lumonSceneFeedbackUpdateRenderer = new LumonSceneFeedbackUpdateRenderer(capi, ConfigModSystem.Config, gBufferManager);
        }

        if (lumonSceneOccupancyClipmapUpdateRenderer is null && ConfigModSystem.Config.LumOn.LumonScene.Enabled)
        {
            lumonSceneOccupancyClipmapUpdateRenderer = new LumonSceneOccupancyClipmapUpdateRenderer(capi, ConfigModSystem.Config);
        }

        capi.Logger.Debug("[VGE] LumOnModSystem ensured ({0})", reason);
    }

    private void TryBindWorldProbeClipmap(ICoreClientAPI api, string reason)
    {
        var clipmapManager = api.ModLoader.GetModSystem<WorldProbeModSystem>().GetClipmapBufferManagerOrNull();
        if (clipmapManager is null)
        {
            clipmapManager = api.ModLoader.GetModSystem<WorldProbeModSystem>().EnsureClipmapResources(api, $"bind ({reason})");
        }

        lumOnDebugRenderer?.SetWorldProbeClipmapBufferManager(clipmapManager);
        lumOnRenderer?.SetWorldProbeClipmapBufferManager(clipmapManager);
    }
}
