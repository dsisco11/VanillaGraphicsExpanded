namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Selects existing context-local observation authority before external effect bookkeeping.</summary>
internal static class OpenGLObservationContext
{
    #region Internal API
    /// <summary>Selects the original context and cached capabilities without native state or error discovery.</summary>
    internal static OpenGlStateCache CurrentCache
    {
        get
        {
            // StartPre installs hooks before StartClientSide; menu rendering already has a native window.
            // Existing registrations retain their generation, including explicitly owned headless contexts.
            if (Integration.RenderContextRegistry.Current().Generation == 0)
                Integration.EngineRenderContext.RegisterCurrent();
            var identity = Integration.RenderContextRegistry.CaptureResourceContext();
            var cache = OpenGlStateCache.Current;
            cache.SelectRHIContext(identity.Handle, identity.Generation == 0 ? default : identity);
            // Capability initialization belongs to explicit admission. Discovery here could consume an
            // error produced by the native call that just ran; a new namespace starts with unknown knowledge.
            if (!GpuSupport.HasCachedCapabilitiesForCurrentContext)
                throw new System.InvalidOperationException("External observation requires initialized context capabilities.");
            return cache;
        }
    }
    #endregion
}
