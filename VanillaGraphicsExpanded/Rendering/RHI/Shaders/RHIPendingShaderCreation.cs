using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Owns an unpublished shader request without exposing backend candidate state.</summary>
public sealed class RHIPendingShaderCreation : IDisposable
{
    private readonly IRHIShaderCreation creation;
    #region Public API
    /// <summary>Gets the candidate outcome without polling the driver.</summary>
    public CompletionStatus Status => creation.Status;
    /// <summary>Gets the ready result or original failure diagnostics.</summary>
    public RHICreationResult<RHIShader> Result => creation.Result;
    /// <summary>Gets the original exceptional failure, when present.</summary>
    public Exception? Error => creation.Error;
    /// <summary>Checks deferred completion without exposing a provisional executable.</summary>
    public CompletionStatus TryComplete() => creation.TryComplete();
    /// <summary>Cancels only unpublished candidate ownership.</summary>
    public void Cancel() => creation.Cancel();
    /// <summary>Releases pending ownership; a ready result belongs to its recipient.</summary>
    public void Dispose() => creation.Dispose();
    #endregion
    #region Internal API
    /// <summary>Retains one backend creation operation under the semantic result contract.</summary>
    internal RHIPendingShaderCreation(IRHIShaderCreation creation) => this.creation = creation;
    #endregion
}
