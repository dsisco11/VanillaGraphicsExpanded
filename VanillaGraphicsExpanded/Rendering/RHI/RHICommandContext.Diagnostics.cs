using System.Diagnostics;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Admits optional owner diagnostics without exposing native object selectors.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Labels an existing owner in Debug builds; diagnostic failure never fails resource work.</summary>
    [Conditional("DEBUG")]
    internal static void TryLabelResource(GpuResource resource, string? name)
    {
#if DEBUG
        if (string.IsNullOrWhiteSpace(name)) return;
        try { if (resource.IsValid) Current.backend.LabelResource(resource, name); }
        catch { /* Diagnostics must not turn successful resource work into failure. */ }
#endif
    }
    /// <summary>Opens an optional diagnostic group without exposing a backend-specific scope.</summary>
    internal System.IDisposable? BeginDiagnosticGroup(string name) => backend.BeginDiagnosticGroup(name);
    /// <summary>Runs an explicitly requested existing diagnostic assertion, never an automatic success probe.</summary>
    internal void CheckDiagnosticErrors(string context) => backend.CheckDiagnosticErrors(context);
    #endregion
}
