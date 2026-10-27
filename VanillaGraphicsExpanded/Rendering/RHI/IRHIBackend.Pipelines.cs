namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Creates immutable pipeline application data from already prepared shader generations.</summary>
internal partial interface IRHIBackend
{
    #region Public API
    /// <summary>Returns validated graphics application data for one fixed executable without linking or reflection.</summary>
    RHICreationResult<RHIGraphicsPipeline> CreateGraphicsPipeline(RHIGraphicsPipelineDescription description);
    /// <summary>Returns a ready managed compute realization without another executable allocation.</summary>
    RHICreationResult<RHIComputePipeline> CreateComputePipeline(RHIComputePipelineDescription description);
    #endregion
}
