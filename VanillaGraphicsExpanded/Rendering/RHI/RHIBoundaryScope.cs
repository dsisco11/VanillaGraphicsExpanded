using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns one admitted engine interruption and exactly-once restoration of both native facts and logical RHI selection.</summary>
public sealed class RHIBoundaryScope : IDisposable
{
    private readonly RHICommandContext context;
    private bool consumed;

    #region Public API
    /// <summary>Runs work and preserves its failure alongside independent restoration failures before ending the boundary.</summary>
    public void Run(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(consumed, this);
        context.RequireBoundaryCleanup(this);
        try { Native.Run(operation); }
        finally
        {
            if (Native.IsConsumed) { consumed = true; context.ReleaseBoundary(this); }
        }
    }
    /// <summary>Restores the admitted interruption once; callers with throwing work can use Run to aggregate cleanup failures.</summary>
    public void Dispose()
    {
        if (consumed) return;
        context.RequireBoundaryCleanup(this);
        try { Native.Dispose(); }
        finally
        {
            if (Native.IsConsumed) { consumed = true; context.ReleaseBoundary(this); }
        }
    }
    #endregion

    #region Internal API
    /// <summary>Gets the unchanged admission contract used to reject undeclared work.</summary>
    internal RHIBoundaryDescription Description { get; }
    /// <summary>Gets the specialized existing cache restoration owner.</summary>
    internal EngineBoundaryScope Native { get; }
    /// <summary>Gets the pre-entry desired selections; restoration never activates or replays a shader asset.</summary>
    internal (RHIGraphicsPipelineRef Graphics, RHIComputePipelineRef Compute, RHIResourceBindings? Resources, RHIDynamicState Dynamics) Previous { get; }
    /// <summary>Retains the resolved restoration token and pure logical selections without additional native capture.</summary>
    internal RHIBoundaryScope(RHICommandContext context, RHIBoundaryDescription description, EngineBoundaryScope native,
        (RHIGraphicsPipelineRef, RHIComputePipelineRef, RHIResourceBindings?, RHIDynamicState) previous)
    { this.context = context; Description = description; Native = native; Previous = previous; }
    #endregion
}
