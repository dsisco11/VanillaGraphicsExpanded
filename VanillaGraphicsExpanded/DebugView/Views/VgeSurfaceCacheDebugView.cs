using System;

using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    #region Surface cache viewer

    // Matches renderLumonSceneIrradianceDebug, including its ambiguous black output.
    private const string SurfaceCacheIrradianceLegend =
        "<b>Cached indirect irradiance</b><br/>"
        + "Lighting is tonemapped for display.<br/>"
        + "<font color=\"#cc00cc\">■</font> Magenta: visible geometry has no patch.<br/>"
        + "<font color=\"#ff0000\">■</font> Red: page is nonresident.<br/>"
        + "<font color=\"#ffff00\">■</font> Yellow: surface capture is pending.<br/>"
        + "<font color=\"#3366ff\">■</font> Blue / blue tint: relighting is pending.<br/>"
        + "<font color=\"#330033\">■</font> Dark purple: surface cache is unavailable.<br/>"
        + "Black: zero irradiance, sky, or no allocated/usable page.<br/>"
        + "Use Page Ready to distinguish page readiness from dark lighting.";

    /// <summary>Exposes cached surface lighting and its supporting residency diagnostics on visible geometry.</summary>
    private static DebugViewDefinition CreateSurfaceCacheDebugView()
        => new(
            id: SurfaceCacheViewId,
            name: "Surface Cache",
            category: CategoryScenes,
            description: "LumOn cached surface irradiance on scene geometry. Irradiance is tonemapped; magenta means no patch, red means nonresident, yellow means capture pending, and a blue tint means relighting. Black can mean zero lighting or no allocated page; use Page Ready to distinguish them.",
            registerRenderer: ctx =>
            {
                // Restore only this viewer's modes, so a previous unrelated view cannot
                // silently replace the irradiance preview when this viewer is selected.
                LumOnDebugMode? saved = ctx.Config.Debug.DebugViews.ActiveExclusiveLumOnDebugMode;
                if (saved.HasValue && Array.IndexOf(SurfaceCacheModes, saved.Value) >= 0)
                {
                    SurfaceCacheViewState.Instance.SetSelectedMode(saved.Value);
                }

                ctx.Config.LumOn.DebugMode = SurfaceCacheViewState.Instance.GetSelectedModeOrDefault();
                return new ActionDisposable(() => ctx.Config.LumOn.DebugMode = LumOnDebugMode.Off);
            },
            activationMode: DebugViewActivationMode.Exclusive,
            availability: ctx => !ctx.Config.LumOn.Enabled
                ? DebugViewAvailability.Unavailable("LumOn is disabled in config.")
                : !ctx.Config.LumOn.LumonScene.Enabled
                    ? DebugViewAvailability.Unavailable("LumonScene surface caching is disabled in config.")
                    : DebugViewAvailability.Available(),
            createPanel: ctx => new LumOnDebugPanel(
                SurfaceCacheViewId, ctx.Capi, ctx.Config, SurfaceCacheViewState.Instance, SurfaceCacheModes));

    private static readonly LumOnDebugMode[] SurfaceCacheModes =
    [
        LumOnDebugMode.LumonSceneIrradiance,
        LumOnDebugMode.LumonScenePageReady,
        LumOnDebugMode.LumonSceneMaterial,
        LumOnDebugMode.LumonScenePatchUv,
    ];

    /// <summary>Remembers the selected surface-cache diagnostic independently of other viewers.</summary>
    private sealed class SurfaceCacheViewState : LumOnDebugViewStateBase
    {
        public static readonly SurfaceCacheViewState Instance = new();

        /// <summary>Starts the viewer with the cached irradiance projected onto visible surfaces.</summary>
        private SurfaceCacheViewState() : base(LumOnDebugMode.LumonSceneIrradiance) { }
    }

    #endregion
}
