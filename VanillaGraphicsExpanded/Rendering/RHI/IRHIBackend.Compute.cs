namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Executes compute commands against fixed realizations without asset lookup or relinking.</summary>
internal partial interface IRHIBackend
{
    #region Public API
    /// <summary>Validates cached limits and dispatches direct group counts once.</summary>
    void Dispatch(RHIComputePipelineRef pipeline, in RHIDispatch dispatch, RHIResourceBindings? resources);
    /// <summary>Validates argument descriptors and executes indirect work without inspecting GPU-produced values.</summary>
    void DispatchIndirect(RHIComputePipelineRef pipeline, in RHIBufferRange arguments, RHIResourceBindings? resources);
    #endregion
}
