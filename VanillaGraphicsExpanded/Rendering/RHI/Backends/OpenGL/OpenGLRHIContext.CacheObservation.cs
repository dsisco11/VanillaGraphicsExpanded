using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Connects existing native cache observation and deletion effects to its admitted implementation.</summary>
internal static partial class OpenGLRHIContext
{
    #region Internal API
    /// <summary>Forwards deletion effects only to an already admitted matching cache.</summary>
    internal static void NotifyResourceDeleted(OpenGlStateCache cache, GpuResourceKind kind, int id)
    {
        if (backend is null || !backend.OwnsCache(cache)) return;
        backend.InvalidateGeometryRealizations(kind, id);
        backend.InvalidatePassTargets(kind, id);
    }
    /// <summary>Reads unknown native selector knowledge for the existing cache collaborator.</summary>
    internal static int ReadImageSelector(GetPName parameter)
    { _ = Current; return backend!.ReadImageSelector(parameter); }
    /// <summary>Acquires unknown transform-feedback activity at an explicit engine boundary.</summary>
    internal static bool ReadTransformFeedbackActive()
    { _ = Current; return backend!.ReadTransformFeedbackActive(); }
    #endregion
}
