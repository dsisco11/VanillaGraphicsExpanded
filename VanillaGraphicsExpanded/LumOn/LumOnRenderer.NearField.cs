using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Captures the same near-field geometry generation for both paired trace branches.</summary>
public partial class LumOnRenderer
{
    private ITraceGeometrySceneProvider? nearFieldProvider;
    private TraceGeometryGpuScene? nearFieldScene;
    private long nearFieldRevision = -1;
    private ISurfaceLightingProvider? surfaceLightingProvider;
    private SurfaceLightingSnapshot? surfaceLighting;
    private SurfaceLightingBindings? surfaceLightingBindings;
    private long surfaceLightingRevision = -1;

    #region Published Scene
    /// <summary>Injects the owner of coherent outgoing radiance used at geometry hits.</summary>
    internal void SetSurfaceLightingProvider(ISurfaceLightingProvider? provider) => surfaceLightingProvider = provider;

    /// <summary>Injects a provider owned by scene composition rather than discovering renderers while drawing.</summary>
    internal void SetNearFieldSceneProvider(ITraceGeometrySceneProvider? provider) => nearFieldProvider = provider;

    /// <summary>Invalidates temporal histories when published geometry or its mapping changes.</summary>
    private void PrepareNearFieldScene()
    {
        var scene = nearFieldProvider?.PrepareScene();
        long revision = scene?.Revision ?? -1;
        if (!ReferenceEquals(scene, nearFieldScene) || revision != nearFieldRevision)
            isFirstFrame = true;
        nearFieldScene = scene;
        nearFieldRevision = revision;
        surfaceLighting = surfaceLightingProvider != null && surfaceLightingProvider.TryGetSurfaceLighting(out var lighting)
            ? lighting : null;
        long dependency = surfaceLighting?.DependencyRevision ?? -1;
        if (dependency != surfaceLightingRevision) isFirstFrame = true;
        surfaceLightingRevision = dependency;
    }
    #endregion
}
