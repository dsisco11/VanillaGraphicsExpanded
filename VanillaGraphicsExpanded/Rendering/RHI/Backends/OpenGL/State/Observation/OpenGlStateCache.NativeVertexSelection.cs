namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates established VAO selection without treating selection as a configuration mutation.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles the completed native transition using local acceptance and original-context facts.</summary>
    internal static void ObserveVertexSelection(int array)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        bool established = cache.HasEstablishedNativeIdentity(GpuResourceKind.VertexArray, array);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();

        if (!cache.OwnsKnowledgeFor(origin)) return;
        cache.currentVao = established
            && Integration.RenderContextRegistry.IsCurrentResourceContext(origin) ? array : null;
    }
    #endregion

}
