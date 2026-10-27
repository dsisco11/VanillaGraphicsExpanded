namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines optional owner diagnostics independently of native object kinds and IDs.</summary>
internal partial interface IRHIResourceExecution
{
    #region Resource diagnostics
    /// <summary>Applies a best-effort label after original-context and association admission.</summary>
    void LabelResource(GpuResource resource, string? name);
    /// <summary>Opens an optional Debug-only diagnostic scope with backend-owned cleanup.</summary>
    System.IDisposable? BeginDiagnosticGroup(string name);
    /// <summary>Performs an explicit diagnostic assertion without changing ordinary command policy.</summary>
    void CheckDiagnosticErrors(string context);
    #endregion
}
