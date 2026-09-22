using System;

using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Diagnostics;
using VanillaGraphicsExpanded.WorldPartition;
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

    internal TraceGeometryRuntimeMetrics? GeometryMetrics => traceGeometryRenderer?.Metrics;
    private LumOnDebugRenderer? lumOnDebugRenderer;
    private LumonSceneFeedbackUpdateRenderer? lumonSceneFeedbackUpdateRenderer;
    private TraceGeometryRenderer? traceGeometryRenderer;
    private LumonSceneRelightUpdateRenderer? lumonSceneRelightUpdateRenderer;
    private LumOnTerrainBridgeUpdateRenderer? lumOnTerrainBridgeUpdateRenderer;

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

        ConfigModSystem.Config.Sanitize();
        lastLiveConfigSnapshot = LumOnLiveConfigSnapshot.From(ConfigModSystem.Config);

        EnsureInitializedIfReady("startup");

        EnsureLumOnStatsPanelInitialized("startup");
        if (ConfigModSystem.Config.Debug.LumOnStatsOverlayEnabled)
        {
            lumOnStatsPanel?.Show();
        }
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

    internal bool IsLumOnRuntimeSelfCheckEnabled()
        => ConfigModSystem.Config.Debug.LumOnRuntimeSelfCheckEnabled;

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
            ConfigModSystem.Config.Debug.LumOnStatsOverlayEnabled = false;
        }
        else
        {
            lumOnStatsPanel.Show();
            ConfigModSystem.Config.Debug.LumOnStatsOverlayEnabled = true;
        }

        // Persist the debug choice so the overlay stays enabled across runs.
        try
        {
            capi.StoreModConfig(ConfigModSystem.Config, Constants.ConfigFileName);
        }
        catch
        {
            // ignore persistence failures
        }
    }

    internal void ToggleLumOnRuntimeSelfCheck()
    {
        if (capi is null)
        {
            return;
        }

        ConfigModSystem.Config.Debug.LumOnRuntimeSelfCheckEnabled = !ConfigModSystem.Config.Debug.LumOnRuntimeSelfCheckEnabled;

        try
        {
            capi.StoreModConfig(ConfigModSystem.Config, Constants.ConfigFileName);
        }
        catch
        {
            // ignore persistence failures
        }
    }

    internal string[] GetLumOnStatsOverlayLines()
    {
        if (!ConfigModSystem.Config.LumOn.Enabled)
        {
            return ["LumOn: Disabled", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty];
        }

        if (lumOnRenderer is null)
        {
            return ["LumOn: Enabled (not initialized)", "Waiting for renderer dependencies...", string.Empty, string.Empty, "TS: init", string.Empty];
        }

        string[] baseLines = lumOnRenderer.DebugCounters.GetDebugLines();

        if (ConfigModSystem.Config.Debug.LumOnRuntimeSelfCheckEnabled)
        {
            return
            [
                (uint)0 < (uint)baseLines.Length ? baseLines[0] : "LumOn: (no stats)",
                (uint)1 < (uint)baseLines.Length ? baseLines[1] : string.Empty,
                GetTraceSceneStatusLineSafe(),
                (uint)3 < (uint)baseLines.Length ? baseLines[3] : string.Empty,
                GetLumonSceneSurfaceCacheStatusLineSafe(),
                GetLumonSceneRelightStatusLineSafe(),
            ];
        }

        string[] lines = new string[6];
        for (int i = 0; i < 4; i++)
        {
            lines[i] = (uint)i < (uint)baseLines.Length ? baseLines[i] : string.Empty;
        }

        // TraceScene (Phase 23) status as a dedicated line (row 5), keep row 6 free for future.
        lines[4] = GetTraceSceneStatusLineSafe();
        lines[5] = string.Empty;

        return lines;
    }

    internal string GetTraceSceneSchedulerDumpSafe(int topN = 64)
    {
        try
        {
            if (!ConfigModSystem.Config.LumOn.LumonScene.Enabled)
            {
                return "TS scheduler: off";
            }

            if (traceGeometryRenderer is null)
            {
                return "TS scheduler: init";
            }

            return traceGeometryRenderer.DumpTraceSceneSchedulerState(topN);
        }
        catch
        {
            return "TS scheduler: error";
        }
    }

    internal string GetLumonSceneSchedulerDumpSafe(int topN = 64)
    {
        try
        {
            if (!ConfigModSystem.Config.LumOn.LumonScene.Enabled)
            {
                return "LS scheduler: off";
            }

            if (lumonSceneFeedbackUpdateRenderer is null)
            {
                return "LS scheduler: init";
            }

            return lumonSceneFeedbackUpdateRenderer.DumpNearRegionSchedulerState(topN);
        }
        catch
        {
            return "LS scheduler: error";
        }
    }

    internal LumonSceneFeedbackUpdateRenderer? GetLumonSceneFeedbackUpdateRendererOrNull()
        => lumonSceneFeedbackUpdateRenderer;

    private string GetLumonSceneSurfaceCacheStatusLineSafe()
    {
        try
        {
            if (!ConfigModSystem.Config.LumOn.LumonScene.Enabled)
            {
                return "LS: off";
            }

            if (lumonSceneFeedbackUpdateRenderer is null)
            {
                return "LS: init";
            }

            return lumonSceneFeedbackUpdateRenderer.TryGetSelfCheckLine(out string line) ? line : "LS: not-ready";
        }
        catch
        {
            return "LS: error";
        }
    }

    private string GetLumonSceneRelightStatusLineSafe()
    {
        try
        {
            if (!ConfigModSystem.Config.LumOn.LumonScene.Enabled)
            {
                return "LSR: off";
            }

            if (lumonSceneRelightUpdateRenderer is null)
            {
                return "LSR: init";
            }

            return lumonSceneRelightUpdateRenderer.TryGetSelfCheckLine(out string line) ? line : "LSR: not-ready";
        }
        catch
        {
            return "LSR: error";
        }
    }

    private string GetTraceSceneStatusLineSafe()
    {
        try
        {
            if (!ConfigModSystem.Config.LumOn.Enabled)
            {
                return "Shared geometry: off";
            }

            if (traceGeometryRenderer is null)
            {
                return "TS: init";
            }

            var m = GeometryMetrics;
            return m == null ? "Shared geometry: unavailable" : $"Shared geometry: required {m.Residency.Required}, ready {m.Residency.Ready}, sources {m.SourceReads}, workers {m.SourceInFlight}, uploaded {m.UploadedBytes / 1048576d:F1} MiB";
        }
        catch { return "Shared geometry: error"; }
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
            PartitionCoordinator worldPartition = clientApi.ModLoader.GetModSystem<WorldPartitionModSystem>().GetCoordinator();
            lumonSceneFeedbackUpdateRenderer = new LumonSceneFeedbackUpdateRenderer(clientApi, ConfigModSystem.Config, gBufferManager, worldPartition);
        }
        lumOnDebugRenderer?.SetLumonSceneFeedbackUpdateRenderer(lumonSceneFeedbackUpdateRenderer);

        if (current.LumOnEnabled
            && traceGeometryRenderer is null)
        {
            PartitionCoordinator worldPartition = clientApi.ModLoader.GetModSystem<WorldPartitionModSystem>().GetCoordinator();
            traceGeometryRenderer = new TraceGeometryRenderer(clientApi, ConfigModSystem.Config, clientApi.ModLoader.GetModSystem<WorldPartitionModSystem>());
        }
        lumOnDebugRenderer?.SetTraceGeometryRenderer(traceGeometryRenderer);
        lumOnRenderer?.SetNearFieldSceneProvider(traceGeometryRenderer);
        lumOnDebugRenderer?.SetNearFieldSceneProvider(traceGeometryRenderer);
        lumonSceneFeedbackUpdateRenderer?.SetTraceGeometryRenderer(traceGeometryRenderer);

        if (current.LumOnEnabled
            && ConfigModSystem.Config.LumOn.LumonScene.Enabled
            && lumonSceneRelightUpdateRenderer is null
            && lumonSceneFeedbackUpdateRenderer is not null
            && traceGeometryRenderer is not null)
        {
            lumonSceneRelightUpdateRenderer = new LumonSceneRelightUpdateRenderer(
                clientApi,
                ConfigModSystem.Config,
                lumonSceneFeedbackUpdateRenderer,
                traceGeometryRenderer);
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        if (capi is not null)
        {
        }

        if (commonEvents is not null)
        {
            commonEvents = null;
        }

        lumOnStatsPanel?.Dispose();
        lumOnStatsPanel = null;

        lumOnDebugRenderer?.Dispose();
        lumOnDebugRenderer = null;

        lumonSceneFeedbackUpdateRenderer?.Dispose();
        lumonSceneFeedbackUpdateRenderer = null;

        traceGeometryRenderer?.Dispose();
        traceGeometryRenderer = null;

        lumonSceneRelightUpdateRenderer?.Dispose();
        lumonSceneRelightUpdateRenderer = null;

        lumOnTerrainBridgeUpdateRenderer?.Dispose();
        lumOnTerrainBridgeUpdateRenderer = null;



        lumOnRenderer?.Dispose();
        lumOnRenderer = null;

        lumOnBufferManager?.Dispose();
        lumOnBufferManager = null;

        gBufferManager = null;
        directLightingBufferManager = null;
        capi = null;
        lastLiveConfigSnapshot = null;
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
            PartitionCoordinator worldPartition = capi.ModLoader.GetModSystem<WorldPartitionModSystem>().GetCoordinator();
            lumonSceneFeedbackUpdateRenderer = new LumonSceneFeedbackUpdateRenderer(capi, ConfigModSystem.Config, gBufferManager, worldPartition);
        }
        lumOnDebugRenderer?.SetLumonSceneFeedbackUpdateRenderer(lumonSceneFeedbackUpdateRenderer);

        if (traceGeometryRenderer is null)
        {
            PartitionCoordinator worldPartition = capi.ModLoader.GetModSystem<WorldPartitionModSystem>().GetCoordinator();
            traceGeometryRenderer = new TraceGeometryRenderer(capi, ConfigModSystem.Config, capi.ModLoader.GetModSystem<WorldPartitionModSystem>());
        }
        lumOnDebugRenderer?.SetTraceGeometryRenderer(traceGeometryRenderer);
        lumOnRenderer?.SetNearFieldSceneProvider(traceGeometryRenderer);
        lumOnDebugRenderer?.SetNearFieldSceneProvider(traceGeometryRenderer);
        lumonSceneFeedbackUpdateRenderer?.SetTraceGeometryRenderer(traceGeometryRenderer);

        if (lumOnTerrainBridgeUpdateRenderer is null && ConfigModSystem.Config.LumOn.LumonScene.Enabled)
        {
            lumOnTerrainBridgeUpdateRenderer = new LumOnTerrainBridgeUpdateRenderer(capi, ConfigModSystem.Config);
        }

        if (lumonSceneRelightUpdateRenderer is null
            && ConfigModSystem.Config.LumOn.LumonScene.Enabled
            && lumonSceneFeedbackUpdateRenderer is not null
            && traceGeometryRenderer is not null)
        {
            lumonSceneRelightUpdateRenderer = new LumonSceneRelightUpdateRenderer(
                capi,
                ConfigModSystem.Config,
                lumonSceneFeedbackUpdateRenderer,
                traceGeometryRenderer);
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
