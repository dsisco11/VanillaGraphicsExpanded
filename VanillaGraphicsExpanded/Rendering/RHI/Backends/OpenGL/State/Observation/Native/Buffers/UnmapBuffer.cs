namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reconciles unmapping without treating corruption as a descriptor change.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles supplied native effects without descriptor acquisition or repeated dispatch.</summary>
    [InterceptOpenGL("glUnmapBuffer")]
    [InterceptOpenGL("glUnmapBufferARB")]
    internal static void ObserveUnmapBuffer(int selector, byte result)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        int id = cache.ResolveObservedBuffer(unchecked((int)selector), false, false);
        // FALSE can mean corruption after a valid unmap, or rejection of an unmapped object.
        // Both leave an established unmapped object unmapped; unknown history stays unknown.
        if (id != 0 && (result != 0 || cache.bufferMappings.ContainsKey(id))) cache.EstablishBufferUnmapped(id);
    }
    #endregion
}
