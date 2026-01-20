using System;

using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

public sealed class LumOnModSystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? capi;

    private GBufferManager? gBufferManager;
    private DirectLightingBufferManager? directLightingBufferManager;

    private LumOnBufferManager? lumOnBufferManager;
    private LumOnRenderer? lumOnRenderer;
    private LumOnDebugRenderer? lumOnDebugRenderer;

    private LumOnLiveConfigSnapshot? lastLiveConfigSnapshot;

    private readonly record struct LumOnLiveConfigSnapshot(
        bool LumOnEnabled,
        int ProbeSpacingPx,
        bool HalfResolution)
    {
        public static LumOnLiveConfigSnapshot From(LumOnConfig cfg)
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

        ConfigModSystem.Config.Sanitize();
        lastLiveConfigSnapshot = LumOnLiveConfigSnapshot.From(ConfigModSystem.Config);

        EnsureInitializedIfReady("startup");
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
    }

    public override void Dispose()
    {
        base.Dispose();

        lumOnDebugRenderer?.Dispose();
        lumOnDebugRenderer = null;

        lumOnRenderer?.Dispose();
        lumOnRenderer = null;

        lumOnBufferManager?.Dispose();
        lumOnBufferManager = null;

        gBufferManager = null;
        directLightingBufferManager = null;
        capi = null;
        lastLiveConfigSnapshot = null;
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
