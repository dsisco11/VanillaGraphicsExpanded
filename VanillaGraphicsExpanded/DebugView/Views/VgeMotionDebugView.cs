using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateMotionDebugView()
        => CreateLumOnModeSelectorDebugView(
            id: MotionDebugViewId,
            name: "Debug Motion",
            category: CategoryMotion,
            description: "Velocity/motion-vector related debug overlays.",
            viewState: MotionDebugViewState.Instance,
            allowedModes:
            [
                LumOnDebugMode.VelocityMagnitude,
                LumOnDebugMode.VelocityValidity,
                LumOnDebugMode.VelocityPrevUv,
            ]);

    private sealed class MotionDebugViewState : LumOnDebugViewStateBase
    {
        public static readonly MotionDebugViewState Instance = new(defaultMode: LumOnDebugMode.VelocityMagnitude);
        private MotionDebugViewState(LumOnDebugMode defaultMode) : base(defaultMode) { }
    }
}
