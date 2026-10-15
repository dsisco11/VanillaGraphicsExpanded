using System;

using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Registers the built-in rendering tools panel.</summary>
public static partial class VgeBuiltInDebugViews
{
    #region Private
    /// <summary>Declares the tools panel independently of diagnostic renderers.</summary>
    private static DebugViewDefinition CreateToolsView()
        => new(
            id: ToolsViewId,
            name: "Tools",
            category: CategoryTools,
            description: "State-aware debug tools not covered by other debug views.",
            registerRenderer: _ => new ActionDisposable(() => { }),
            activationMode: DebugViewActivationMode.Toggle,
            createPanel: ctx => new ToolsPanel(ctx.Capi));

    /// <summary>Hosts explicit diagnostic actions and runtime debug toggles.</summary>
    private sealed class ToolsPanel : DebugViewPanelBase
    {
        private readonly ICoreClientAPI capi;

        private GuiComposer? composer;

        private bool lastViewerOpen;
        private bool lastLumOnEnabled;
        private bool lastStatsShown;
        private bool lastSelfCheckEnabled;

        #region Public API
        /// <summary>Borrows the active client for menu callbacks.</summary>
        public ToolsPanel(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>Refreshes toggle labels as runtime state changes.</summary>
        public override bool WantsGameTick => true;

        /// <summary>Builds tool buttons, queuing GPU diagnostics until scene publication.</summary>
        public override void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix)
        {
            this.composer = composer;

            const double rowH = 28;
            const double rowGapY = 8;
            const double buttonW = 340;

            var fontLabel = CairoFont.WhiteSmallText();

            ElementBounds b0 = ElementBounds.Fixed(0, 0, buttonW, rowH).WithParent(bounds);
            ElementBounds b1 = ElementBounds.Fixed(0, rowH + rowGapY, buttonW, rowH).WithParent(bounds);
            ElementBounds b2 = ElementBounds.Fixed(0, (rowH + rowGapY) * 2, buttonW, rowH).WithParent(bounds);
            ElementBounds b3 = ElementBounds.Fixed(0, (rowH + rowGapY) * 3, buttonW, rowH).WithParent(bounds);
            ElementBounds b4 = ElementBounds.Fixed(0, (rowH + rowGapY) * 4, buttonW, rowH).WithParent(bounds);
            ElementBounds b5 = ElementBounds.Fixed(0, (rowH + rowGapY) * 5, buttonW, rowH).WithParent(bounds);

            bool viewerOpen = VgeDebugViewerManager.IsDialogOpen();

            var lumOn = capi.ModLoader.GetModSystem<LumOnModSystem>();
            bool lumOnEnabled = lumOn.IsLumOnEnabled();
            bool statsShown = lumOn.IsLumOnStatsOverlayShown();
            bool selfCheckEnabled = lumOn.IsLumOnRuntimeSelfCheckEnabled();

            lastViewerOpen = viewerOpen;
            lastLumOnEnabled = lumOnEnabled;
            lastStatsShown = statsShown;
            lastSelfCheckEnabled = selfCheckEnabled;

            composer
                .AddSmallButton(
                    text: $"Debug Viewer: {(viewerOpen ? "Open" : "Closed")}",
                    onClick: () =>
                    {
                        VgeDebugViewerManager.ToggleDialog();
                        TryRecompose();
                        return true;
                    },
                    bounds: b0,
                    style: EnumButtonStyle.Normal,
                    key: $"{keyPrefix}-viewer")
                .AddSmallButton(
                    text: $"LumOn: {(lumOnEnabled ? "Enabled" : "Disabled")}",
                    onClick: () =>
                    {
                        lumOn.ToggleLumOnEnabled();
                        TryRecompose();
                        return true;
                    },
                    bounds: b1,
                    style: EnumButtonStyle.Normal,
                    key: $"{keyPrefix}-lumon")
                .AddSmallButton(
                    text: $"LumOn Stats: {(statsShown ? "Shown" : "Hidden")}",
                    onClick: () =>
                    {
                        lumOn.ToggleLumOnStatsOverlay();
                        TryRecompose();
                        return true;
                    },
                    bounds: b2,
                    style: EnumButtonStyle.Normal,
                    key: $"{keyPrefix}-lumonstats");

            composer
                .AddSmallButton(
                    text: $"LumOn SelfCheck: {(selfCheckEnabled ? "On" : "Off")}",
                    onClick: () =>
                    {
                        lumOn.ToggleLumOnRuntimeSelfCheck();
                        TryRecompose();
                        return true;
                    },
                    bounds: b3,
                    style: EnumButtonStyle.Normal,
                    key: $"{keyPrefix}-lumonselfcheck");

            composer
                .AddSmallButton(
                    text: "Log LumOn Stats",
                    onClick: () =>
                    {
                        try
                        {
                            string[] lines = lumOn.GetLumOnStatsOverlayLines();
                            string msg = string.Join(" | ", lines ?? Array.Empty<string>()).Trim();
                            capi.Logger.Debug("[VGE] LumOn stats snapshot: {0}", msg);
                        }
                        catch
                        {
                            capi.Logger.Debug("[VGE] LumOn stats snapshot: (error)");
                        }

                        return true;
                    },
                    bounds: b4,
                    style: EnumButtonStyle.Normal,
                    key: $"{keyPrefix}-lumonstatslog");

            composer
                .AddSmallButton(
                    text: "Dump TraceScene Scheduler",
                    onClick: () =>
                    {
                        try
                        {
                            string dump = lumOn.GetTraceSceneSchedulerDumpSafe(topN: 64);
                            capi.Logger.Debug("[VGE] {0}", dump);
                        }
                        catch
                        {
                            capi.Logger.Debug("[VGE] TS scheduler dump: (error)");
                        }

                        return true;
                    },
                    bounds: b5,
                    style: EnumButtonStyle.Normal,
                    key: $"{keyPrefix}-tracescenedump");

            composer
                .AddSmallButton(
                    text: "Dump LumonScene Scheduler",
                    onClick: () =>
                    {
                        try
                        {
                            string dump = lumOn.GetLumonSceneSchedulerDumpSafe(topN: 64);
                            capi.Logger.Debug("[VGE] {0}", dump);
                        }
                        catch
                        {
                            capi.Logger.Debug("[VGE] LS scheduler dump: (error)");
                        }

                        return true;
                    },
                    bounds: b5.FlatCopy().WithFixedOffset(0, 25),
                    style: EnumButtonStyle.Normal,
                    key: $"{keyPrefix}-lumonscenedump");

            composer.AddSmallButton(
                text: "Log Exposure Snapshot",
                onClick: () =>
                {
                    PBR.Postprocessing.Diagnostics.ExposureSnapshotLog.Request();
                    return true;
                },
                bounds: b5.FlatCopy().WithFixedOffset(0, rowH + rowGapY + rowH + rowGapY),
                style: EnumButtonStyle.Normal,
                key: $"{keyPrefix}-exposuresnapshot");

            _ = fontLabel;
        }

        /// <summary>Recomposes only when displayed toggle state changes.</summary>
        public override void OnGameTick(float dt)
        {
            var lumOn = capi.ModLoader.GetModSystem<LumOnModSystem>();

            bool viewerOpen = VgeDebugViewerManager.IsDialogOpen();
            bool lumOnEnabled = lumOn.IsLumOnEnabled();
            bool statsShown = lumOn.IsLumOnStatsOverlayShown();
            bool selfCheckEnabled = lumOn.IsLumOnRuntimeSelfCheckEnabled();

            if (viewerOpen != lastViewerOpen
                || lumOnEnabled != lastLumOnEnabled
                || statsShown != lastStatsShown
                || selfCheckEnabled != lastSelfCheckEnabled)
            {
                lastViewerOpen = viewerOpen;
                lastLumOnEnabled = lumOnEnabled;
                lastStatsShown = statsShown;
                lastSelfCheckEnabled = selfCheckEnabled;
                TryRecompose();
            }
        }

        #endregion
        #region Private
        /// <summary>Refreshes labels without propagating GUI teardown failures.</summary>
        private void TryRecompose()
        {
            try
            {
                composer?.ReCompose();
            }
            catch
            {
                // Ignore UI refresh failures.
            }
        }
        #endregion
    }
    #endregion
}
