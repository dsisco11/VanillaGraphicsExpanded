using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateGBufferDebugView()
        => CreateLumOnModeSelectorDebugView(
            id: GBufferDebugViewId,
            name: "Debug G-Buffers",
            category: CategoryGBuffer,
            description: "G-buffer and material-related debug overlays.",
            viewState: GBufferDebugViewState.Instance,
            allowedModes:
            [
                LumOnDebugMode.SceneDepth,
                LumOnDebugMode.SceneNormal,
                LumOnDebugMode.MaterialBands,
                LumOnDebugMode.VgeNormalDepthAtlas,
                LumOnDebugMode.PomMetrics,

                // LumonScene (surface cache) + TraceScene (occupancy) + combined overview.
                LumOnDebugMode.LumonScenePageReady,
                LumOnDebugMode.LumonScenePatchUv,
                LumOnDebugMode.LumonSceneIrradiance,
                LumOnDebugMode.LumonSceneMaterial,
                LumOnDebugMode.LumonSceneMaterialRoughness,
                LumOnDebugMode.LumonSceneMaterialAtlasAll,
                LumOnDebugMode.LumonSceneChunkSlot,
                LumOnDebugMode.LumonSceneSlotGeneration,
                LumOnDebugMode.LumonScenePageTableOccupancy,
                LumOnDebugMode.TraceSceneBoundsL0,
                LumOnDebugMode.TraceSceneOccupancyL0,
                LumOnDebugMode.TraceScenePayloadL0,
                LumOnDebugMode.LumOnScenesOverview,
            ]);

    private sealed class GBufferDebugViewState : LumOnDebugViewStateBase
    {
        public static readonly GBufferDebugViewState Instance = new(defaultMode: LumOnDebugMode.SceneNormal);
        private GBufferDebugViewState(LumOnDebugMode defaultMode) : base(defaultMode) { }
    }
}
