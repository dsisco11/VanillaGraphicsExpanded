namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Routes manager execution exclusions without native admission or canonical publication.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Rejects unrelated reentry without selecting native execution or acquiring metadata.</summary>
    internal static void RequireResourceOperationAvailable(GpuResourceKind kind, nint id, IRHIResourceOperation? participant = null)
        => ResourceOperations.RequireResourceOperationAvailable(kind, id, participant);

    /// <summary>Captures the backend's existing exclusion while the manager retains its metadata transaction.</summary>
    internal static IRHIResourceOperation EnterResourceOperation(GpuResourceHandle handle)
        => ResourceOperations.EnterResourceOperation(handle);

    /// <summary>Cancels the original exclusion after deletion without reentering native admission.</summary>
    internal static void CancelResourceOperation(GpuResourceKind kind, nint id)
        => ResourceOperations.CancelResourceOperation(kind, id);
    #endregion
}
