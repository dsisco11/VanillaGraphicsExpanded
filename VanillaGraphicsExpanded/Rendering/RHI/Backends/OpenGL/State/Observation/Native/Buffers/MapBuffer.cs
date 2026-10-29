namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains mapping activity from the actual native result; null pointers establish no mapping.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles supplied native effects without descriptor acquisition or repeated dispatch.</summary>
    [InterceptOpenGL("glMapBuffer")]
    [InterceptOpenGL("glMapBufferARB")]
    internal static void ObserveMapBuffer(int selector, int access, nint result)
    {
        if (result == 0 || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        int id = cache.ResolveObservedBuffer(unchecked((int)selector), false, false);
        if (id != 0 && result != 0) cache.RecordBufferMapped(id);
    }
    #endregion
}
