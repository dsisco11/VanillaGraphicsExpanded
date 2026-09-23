using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Captures the same near-field geometry generation for both paired trace branches.</summary>
public partial class LumOnRenderer
{
    private ITraceGeometrySceneProvider? nearFieldProvider;
    private TraceGeometryGpuScene? nearFieldScene;
    private readonly ProbeLightingHistoryDependencies historyDependencies = new();
    private ISurfaceLightingProvider? surfaceLightingProvider;
    private SurfaceLightingSnapshot? surfaceLighting;
    private SurfaceLightingBindings? surfaceLightingBindings;

    #region Published Scene
    /// <summary>Injects the owner of coherent outgoing radiance used at geometry hits.</summary>
    internal void SetSurfaceLightingProvider(ISurfaceLightingProvider? provider) => surfaceLightingProvider = provider;

    /// <summary>Injects a provider owned by scene composition rather than discovering renderers while drawing.</summary>
    internal void SetNearFieldSceneProvider(ITraceGeometrySceneProvider? provider) => nearFieldProvider = provider;

    /// <summary>Invalidates temporal histories when published geometry or its mapping changes.</summary>
    private void PrepareNearFieldScene()
    {
        nearFieldScene = nearFieldProvider?.PrepareScene();
        surfaceLighting = surfaceLightingProvider != null && surfaceLightingProvider.TryGetSurfaceLighting(out var lighting)
            ? lighting : null;
        if (historyDependencies.Synchronize(nearFieldScene, surfaceLighting)) isFirstFrame = true;
    }
    #endregion
}
