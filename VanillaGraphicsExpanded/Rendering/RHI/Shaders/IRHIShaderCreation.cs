using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Owns an unpublished backend candidate and its cleanup without exposing native stage state.</summary>
internal interface IRHIShaderCreation : IDisposable
{
    /// <summary>Gets the established completion state.</summary>
    CompletionStatus Status { get; }
    /// <summary>Gets the completed resource or original diagnostics.</summary>
    RHICreationResult<RHIShader> Result { get; }
    /// <summary>Gets an exceptional creation failure when present.</summary>
    Exception? Error { get; }
    /// <summary>Observes deferred creation on its originating context.</summary>
    CompletionStatus TryComplete();
    /// <summary>Retires only an unpublished candidate.</summary>
    void Cancel();
}
