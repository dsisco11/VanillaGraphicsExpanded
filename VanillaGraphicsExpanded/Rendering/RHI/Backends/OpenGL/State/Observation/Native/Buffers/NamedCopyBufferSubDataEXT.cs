namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves EXT copy-side realization while leaving descriptor revisions intact.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles supplied native effects without descriptor acquisition or repeated dispatch.</summary>
    [InterceptOpenGL("glNamedCopyBufferSubDataEXT")]
    internal static void ObserveNamedCopyBufferSubDataEXT(uint source, uint destination, nint from, nint to, nint size)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        cache.RealizeNativeBuffer(unchecked((int)source), true);
        cache.RealizeNativeBuffer(unchecked((int)destination), true);
    }
    #endregion
}
