using System;

using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    #region Surface cache viewer

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
