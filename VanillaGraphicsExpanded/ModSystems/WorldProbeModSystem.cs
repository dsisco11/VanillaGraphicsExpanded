using System;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class WorldProbeModSystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? capi;

    private LumOnWorldProbeClipmapBufferManager? clipmapBufferManager;

    private WorldProbeLiveConfigSnapshot? lastSnapshot;

    private readonly record struct WorldProbeLiveConfigSnapshot(
        bool LumOnEnabled,
        float ClipmapBaseSpacing,
        int ClipmapResolution,
        int ClipmapLevels,
        int ClipmapBudgetsHash,
        int ClipmapTraceMaxProbesPerFrame,
        int ClipmapUploadBudgetBytesPerFrame)
    {
        public static WorldProbeLiveConfigSnapshot From(LumOnConfig cfg)
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

        // Snapshot initial config state so live reload deltas are correct.
        ConfigModSystem.Config.Sanitize();
        lastSnapshot = WorldProbeLiveConfigSnapshot.From(ConfigModSystem.Config);

        // If LumOn starts enabled, initialize clipmap resources immediately so the renderer can run Phase 18.
        if (ConfigModSystem.Config.LumOn.Enabled)
        {
            EnsureClipmapResources(api, "startup");
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

        clipmapBufferManager?.Dispose();
        clipmapBufferManager = null;
        capi = null;
        lastSnapshot = null;
    }
}
