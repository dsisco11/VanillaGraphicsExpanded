namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes EXT implicit buffer realization without changing storage for content or query work.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles supplied native effects without descriptor acquisition or repeated dispatch.</summary>
    [InterceptOpenGL("glGetNamedBufferParameterivEXT")]
    internal static void ObserveGetNamedBufferParameterivEXT(uint id, int pname, int* data)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        cache.RealizeNativeBuffer(unchecked((int)id), true);
    }
    #endregion
}
