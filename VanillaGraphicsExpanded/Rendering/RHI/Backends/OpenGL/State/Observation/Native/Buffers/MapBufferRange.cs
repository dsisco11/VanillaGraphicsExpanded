namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains mapping activity from the actual native result; null pointers establish no mapping.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles supplied native effects without descriptor acquisition or repeated dispatch.</summary>
    [InterceptOpenGL("glMapBufferRange")]
    internal static void ObserveMapBufferRange(int selector, nint offset, nint length, int access, nint result)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        int id = cache.ResolveObservedBuffer(unchecked((int)selector), false, false);
        if (id != 0 && result != 0) cache.bufferMappings[id] = true;
    }
    #endregion
}
