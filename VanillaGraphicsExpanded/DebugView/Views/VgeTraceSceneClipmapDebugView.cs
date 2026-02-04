using System;

using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateTraceSceneClipmapDebugView()
        => new(
            id: TraceSceneClipmapViewId,
            name: "TraceScene Clipmap",
            category: CategoryScenes,
            description: "LumOn TraceScene occupancy clipmap debug views (including voxel DDA distance gradient).",
            registerRenderer: ctx =>
            {
                ctx.Config.LumOn.DebugMode = TraceSceneClipmapViewState.Instance.GetSelectedModeOrDefault();
                return new ActionDisposable(() => ctx.Config.LumOn.DebugMode = LumOnDebugMode.Off);
            },
            activationMode: DebugViewActivationMode.Exclusive,
            availability: ctx =>
            {
                if (!ctx.Config.LumOn.Enabled)
                {
                    return DebugViewAvailability.Unavailable("LumOn is disabled in config.");
                }

                if (!ctx.Config.LumOn.LumonScene.Enabled)
                {
                    return DebugViewAvailability.Unavailable("LumonScene/TraceScene is disabled in config.");
                }

                return DebugViewAvailability.Available();
            },
            createPanel: ctx => new LumOnDebugPanel(
                viewId: TraceSceneClipmapViewId,
                capi: ctx.Capi,
                config: ctx.Config,
                viewState: TraceSceneClipmapViewState.Instance,
                allowedModes: AllowedTraceSceneModes));

    private static readonly LumOnDebugMode[] AllowedTraceSceneModes =
    [
        LumOnDebugMode.TraceSceneDdaDistanceL0,
        LumOnDebugMode.TraceSceneBoundsL0,
        LumOnDebugMode.TraceSceneOccupancyL0,
        LumOnDebugMode.TraceScenePayloadL0,
    ];

    private sealed class TraceSceneClipmapViewState : LumOnDebugViewStateBase
    {
        public static readonly TraceSceneClipmapViewState Instance = new();

        private TraceSceneClipmapViewState() : base(defaultMode: LumOnDebugMode.TraceSceneDdaDistanceL0)
        {
        }
    }
}
