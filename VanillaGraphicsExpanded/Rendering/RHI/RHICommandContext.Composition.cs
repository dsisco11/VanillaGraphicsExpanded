namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Selects the installed graphics implementation at the composition boundary.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Gets the execution context admitted for the current native context and thread.</summary>
    public static RHICommandContext Current => Backends.OpenGL.OpenGLRHIContext.Current;
    #endregion
    #region Internal API
    /// <summary>Rejects preparation during a restoration boundary without requiring a native context for CPU-only asset work.</summary>
    internal static void RequirePreparationAdmission() => Backends.OpenGL.OpenGLRHIContext.RequirePreparationAdmission();
    /// <summary>Reports original context/thread admission without exposing native context identity.</summary>
    internal bool IsCurrentContext => backend.IsCurrentContext;
    /// <summary>Rejects owner operations outside this context's originating execution domain.</summary>
    internal void RequireContext() => backend.RequireContext();
    #endregion
}
