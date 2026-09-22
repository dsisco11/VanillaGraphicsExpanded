using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Captures the same near-field geometry generation for both paired trace branches.</summary>
public partial class LumOnRenderer
{
    private ITraceGeometrySceneProvider? nearFieldProvider;
    private TraceGeometryGpuScene? nearFieldScene;
    private long nearFieldRevision = -1;

    #region Published Scene
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
    }
    #endregion
}
