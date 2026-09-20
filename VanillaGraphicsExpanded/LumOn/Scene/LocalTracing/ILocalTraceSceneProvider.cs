namespace VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

/// <summary>Supplies the coherent previously completed scene to both screen-probe trace branches.</summary>
internal interface ILocalTraceSceneProvider
{
    /// <summary>Applies pending invalidation and returns a read-only-for-the-frame GPU scene, or null if unavailable.</summary>
    LocalTraceGpuScene? PrepareLocalTraceScene();
}
