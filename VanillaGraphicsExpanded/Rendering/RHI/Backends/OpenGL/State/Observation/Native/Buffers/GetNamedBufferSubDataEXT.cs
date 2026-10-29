namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes EXT implicit buffer realization without changing storage for content or query work.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles supplied native effects without descriptor acquisition or repeated dispatch.</summary>
    [InterceptOpenGL("glGetNamedBufferSubDataEXT")]
    internal static void ObserveGetNamedBufferSubDataEXT(uint id, nint offset, nint size, nint data)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        cache.RealizeNativeBuffer(unchecked((int)id), true);
    }
    #endregion
}
