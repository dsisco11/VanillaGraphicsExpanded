namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Associates the one thread-local knowledge store with its currently admitted native namespace.</summary>
internal sealed partial class OpenGlStateCache
{
    private nint rhiNativeContext;
    private (nint Handle, long Generation) rhiContextLifetime;

    #region Internal API
    /// <summary>Withdraws executed knowledge when switching contexts instead of reusing another namespace's bindings.</summary>
    internal void SelectRHIContext(nint nativeContext, (nint Handle, long Generation) lifetime)
    {
        if (rhiNativeContext == nativeContext && rhiContextLifetime == lifetime) return;
        InvalidateAll();
        rhiNativeContext = nativeContext;
        rhiContextLifetime = lifetime;
    }
    #endregion
}
