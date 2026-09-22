namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Exposes one current shared geometry generation after rejecting stale dependencies.</summary>
internal interface ITraceGeometrySceneProvider
{
    /// <summary>Returns the current published resources, or unavailable when the source cannot be served.</summary>
    TraceGeometryGpuScene? PrepareScene();
}
