using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Captures the same local geometry generation for both paired trace branches.</summary>
public partial class LumOnRenderer
{
    private ILocalTraceSceneProvider? localTraceProvider;
    private LocalTraceGpuScene? localTraceScene;
    private long localTraceRevision = -1;

    #region Published Scene
    /// <summary>Injects a provider owned by scene composition rather than discovering renderers while drawing.</summary>
    internal void SetLocalTraceSceneProvider(ILocalTraceSceneProvider? provider) => localTraceProvider = provider;

    /// <summary>Invalidates temporal histories when published geometry or its mapping changes.</summary>
    private void PrepareLocalTraceScene()
    {
        var scene = localTraceProvider?.PrepareLocalTraceScene();
        long revision = scene?.Revision ?? -1;
        if (!ReferenceEquals(scene, localTraceScene) || revision != localTraceRevision)
            isFirstFrame = true;
        localTraceScene = scene;
        localTraceRevision = revision;
    }
    #endregion
}
