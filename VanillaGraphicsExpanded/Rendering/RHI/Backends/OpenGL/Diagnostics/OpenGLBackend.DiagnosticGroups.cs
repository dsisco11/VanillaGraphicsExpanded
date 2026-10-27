namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns optional native diagnostic group lifetimes.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public System.IDisposable? BeginDiagnosticGroup(string name)
    {
#if DEBUG
        RequireContext();
        return GlDebug.Group(name);
#else
        return null;
#endif
    }
    #endregion
}
