using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns an unpublished executable candidate until explicit completion or cancellation.</summary>
internal sealed class OpenGLPendingShaderCreation : IRHIShaderCreation
{
    private readonly OpenGLBackend backend;
    #region Public API
    /// <summary>Gets the candidate outcome without polling the driver.</summary>
    public CompletionStatus Status { get; internal set; } = CompletionStatus.Pending;
    /// <summary>Gets the ready creation result or the original failure diagnostics.</summary>
    public RHICreationResult<RHIShader> Result { get; internal set; }
    /// <summary>Gets the original exceptional failure, when one was raised.</summary>
    public Exception? Error { get; internal set; }
    /// <summary>Checks optional native link completion without exposing a partial executable.</summary>
    public CompletionStatus TryComplete() => backend.CompleteShaderCreation(this, false);
    /// <summary>Cancels pending creation and retires only unpublished candidates.</summary>
    public void Cancel()
    {
        if (Transition) throw new InvalidOperationException("Shader creation is already in progress.");
        if (Status == CompletionStatus.Ready) return;
        if (Status == CompletionStatus.Pending) Status = CompletionStatus.Cancelled;
        backend.ReleaseShaderCandidate(this);
    }
    /// <summary>Releases pending ownership; a ready result remains owned by its recipient.</summary>
    public void Dispose() => Cancel();
    #endregion

    #region Internal API
    /// <summary>Gets the frozen creation request.</summary>
    internal RHIShaderDescription Description { get; }
    /// <summary>Retains the original candidate program until ownership transfers on success.</summary>
    internal GpuProgramObject? Program { get; set; }
    /// <summary>Retains temporary stage ownership until link completion or candidate cleanup.</summary>
    internal List<GpuShaderModule> Stages { get; } = new();
    /// <summary>Prevents native callbacks from consuming or cancelling an unfinished transition.</summary>
    internal bool Transition { get; set; }
    /// <summary>Captures context-affine backend services and already frozen inputs.</summary>
    internal OpenGLPendingShaderCreation(OpenGLBackend backend, RHIShaderDescription description)
    { this.backend = backend; Description = description; }
    #endregion
}
