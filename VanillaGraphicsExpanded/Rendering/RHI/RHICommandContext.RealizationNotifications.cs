using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Routes existing native lifetime effects to the originating backend's private realization owners.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Withdraws dependent native realizations without allocating a context or resolving a reused resource ID.</summary>
    internal static void NotifyResourceDeleted(StateCache cache, GpuResourceKind kind, int id)
    {
        if (current?.backend is OpenGLBackend implementation && implementation.OwnsCache(cache))
            {
            implementation.InvalidateGeometryRealizations(kind, id);
            implementation.InvalidatePassTargets(kind, id);
        }
    }
    #endregion
}
