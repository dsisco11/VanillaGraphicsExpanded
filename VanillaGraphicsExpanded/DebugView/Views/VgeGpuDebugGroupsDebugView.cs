using VanillaGraphicsExpanded.Rendering.Profiling;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateGpuDebugGroupsView()
        => new(
            id: GpuDebugGroupsViewId,
            name: "GPU Debug Groups",
            category: CategoryProfiling,
            description: "Emit GL debug groups for GlGpuProfiler scopes (useful in RenderDoc/Nsight).",
            registerRenderer: _ =>
            {
                GlGpuProfiler.Instance.EmitDebugGroups = true;
                return new ActionDisposable(() => GlGpuProfiler.Instance.EmitDebugGroups = false);
            },
            activationMode: DebugViewActivationMode.Toggle);
}
