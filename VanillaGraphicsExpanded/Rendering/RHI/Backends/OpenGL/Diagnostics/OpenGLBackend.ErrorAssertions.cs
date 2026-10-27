namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns explicitly requested native diagnostic assertions.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void CheckDiagnosticErrors(string context)
    {
        RequireContext();
        GlDebug.ThrowIfErrors(context);
    }
    #endregion
}
