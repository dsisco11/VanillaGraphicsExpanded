using VanillaGraphicsExpanded.LumOn.Scene.NearField;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Captures the same near-field geometry generation for both paired trace branches.</summary>
public partial class LumOnRenderer
{
    private INearFieldSceneProvider? nearFieldProvider;
    private NearFieldGpuScene? nearFieldScene;
    private long nearFieldRevision = -1;

    #region Published Scene
    /// <summary>Injects a provider owned by scene composition rather than discovering renderers while drawing.</summary>
    internal void SetNearFieldSceneProvider(INearFieldSceneProvider? provider) => nearFieldProvider = provider;

    /// <summary>Invalidates temporal histories when published geometry or its mapping changes.</summary>
    private void PrepareNearFieldScene()
    {
        var scene = nearFieldProvider?.PrepareNearFieldScene();
        long revision = scene?.Revision ?? -1;
        if (!ReferenceEquals(scene, nearFieldScene) || revision != nearFieldRevision)
            isFirstFrame = true;
        nearFieldScene = scene;
        nearFieldRevision = revision;
    }
    #endregion
}
