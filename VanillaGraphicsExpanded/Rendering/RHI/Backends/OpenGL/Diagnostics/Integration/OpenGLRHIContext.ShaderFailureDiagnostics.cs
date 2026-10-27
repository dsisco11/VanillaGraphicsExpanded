using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns diagnostics at the explicit failed engine-compilation boundary.</summary>
internal static partial class OpenGLRHIContext
{
    #region Internal API
    /// <summary>Reports pending errors only after the engine has already reported compilation failure.</summary>
    internal static void ReportFailedEngineCompilation(Action<string> report)
    {
        // Failed engine compilation can leave errors from subsequent location queries. Preserve the
        // existing bounded recovery report; this does not establish success or run on ordinary commands.
        string errors = GlDebug.GetErrorsString("OpenGL errors observed after failed patched compilation");
        if (errors.Length != 0) report(errors);
    }
    #endregion
}
