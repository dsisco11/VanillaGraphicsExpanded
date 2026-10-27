namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Realizes immutable pipeline descriptions against prepared executable generations.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Creates immutable graphics state while preserving the shader owner's lifetime authority.</summary>
    public RHICreationResult<RHIGraphicsPipeline> CreateGraphicsPipeline(RHIGraphicsPipelineDescription description)
        => backend.CreateGraphicsPipeline(description);
    /// <summary>Creates compute application data while leaving shader ownership with its existing owner.</summary>
    public RHICreationResult<RHIComputePipeline> CreateComputePipeline(RHIComputePipelineDescription description)
        => backend.CreateComputePipeline(description);
    #endregion
}
