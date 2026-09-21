namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Supplies the coherent previously completed scene to both screen-probe trace branches.</summary>
internal interface INearFieldSceneProvider
{
    /// <summary>Applies pending invalidation and returns a read-only-for-the-frame GPU scene, or null if unavailable.</summary>
    NearFieldGpuScene? PrepareNearFieldScene();
}
