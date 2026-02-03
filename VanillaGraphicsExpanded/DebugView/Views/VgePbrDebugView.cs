using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreatePbrDebugView()
        => CreateLumOnModeSelectorDebugView(
            id: PbrDebugViewId,
            name: "Debug PBR",
            category: CategoryPbr,
            description: "PBR direct lighting and composite debug overlays.",
            viewState: PbrDebugViewState.Instance,
            allowedModes:
            [
                LumOnDebugMode.CompositeAO,
                LumOnDebugMode.CompositeIndirectDiffuse,
                LumOnDebugMode.CompositeIndirectSpecular,
                LumOnDebugMode.CompositeMaterial,
                LumOnDebugMode.DirectDiffuse,
                LumOnDebugMode.DirectSpecular,
                LumOnDebugMode.DirectEmissive,
                LumOnDebugMode.DirectTotal,
            ]);

    private sealed class PbrDebugViewState : LumOnDebugViewStateBase
    {
        public static readonly PbrDebugViewState Instance = new(defaultMode: LumOnDebugMode.CompositeAO);
        private PbrDebugViewState(LumOnDebugMode defaultMode) : base(defaultMode) { }
    }
}
