namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines CPU-only execution exclusion before native admission, without metadata ownership.</summary>
internal interface IRHIResourceOperationExecution
{
    /// <summary>Rejects unrelated reentry while permitting the exact explicitly participating operation.</summary>
    void RequireResourceOperationAvailable(GpuResourceKind kind, nint id, IRHIResourceOperation? participant = null);
    /// <summary>Captures an exclusion whose disposal releases only its original backend bookkeeping.</summary>
    IRHIResourceOperation EnterResourceOperation(GpuResourceHandle handle);
    /// <summary>Interrupts original operations after observed deletion, without acquiring a native context.</summary>
    void CancelResourceOperation(GpuResourceKind kind, nint id);
}
