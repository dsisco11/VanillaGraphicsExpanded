using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    /// <summary>Exposes direct-light diagnostics independently of the optional indirect-light system.</summary>
    private static DebugViewDefinition CreatePbrDebugView()
        => CreateLumOnModeSelectorDebugView(
            id: PbrDebugViewId,
            name: "Debug PBR",
            category: CategoryPbr,
            description: "PBR direct lighting debug overlays. Composite views require LumOn.",
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
                LumOnDebugMode.Transmission,
            ],
            requiresLumOn: false);

    private sealed class PbrDebugViewState : LumOnDebugViewStateBase
    {
        public static readonly PbrDebugViewState Instance = new(defaultMode: LumOnDebugMode.DirectDiffuse);
        private PbrDebugViewState(LumOnDebugMode defaultMode) : base(defaultMode) { }
    }
}
