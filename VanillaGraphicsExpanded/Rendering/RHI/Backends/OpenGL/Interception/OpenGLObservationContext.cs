namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Selects existing context-local observation authority without capability discovery.</summary>
internal static class OpenGLObservationContext
{
    #region Internal API
    /// <summary>Selects a live originating cache; optional capability requirements never trigger native discovery.</summary>
    internal static bool TryGet(out OpenGlStateCache cache, out (nint Handle, long Generation) origin,
        bool requireCapabilities = true)
    {
        cache = OpenGlStateCache.Current;
        origin = default;
        // Menu rendering may precede explicit capability admission. Absence is ordinary control flow.
        if (Integration.RenderContextRegistry.Current().Generation == 0
            && !Integration.EngineRenderContext.TryRegisterCurrent()) return false;
        origin = Integration.RenderContextRegistry.CaptureResourceContext();
        cache.SelectRHIContext(origin.Handle, origin);
        return !requireCapabilities || GpuSupport.HasCachedCapabilitiesForCurrentContext;
    }
    #endregion
}
