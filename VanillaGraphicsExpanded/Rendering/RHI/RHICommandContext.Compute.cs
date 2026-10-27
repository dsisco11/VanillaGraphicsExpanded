using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains semantic compute selection and submits immediate work through the owning backend.</summary>
public sealed partial class RHICommandContext
{
    private RHIComputePipelineRef selectedComputePipeline;
    private bool submitting;

    #region Public API
    /// <summary>Selects an exact compute realization without retargeting a mutable asset owner.</summary>
    public void SetPipeline(RHIComputePipelineRef pipeline)
    {
        backend.RequireContext();
        RequireIdleSubmission();
        RequireAdmittedPipeline(pipeline);
        var selected = pipeline.RequirePipeline();
        selectedComputePipeline = pipeline;
        selectedGraphicsPipeline = default;
        if (selectedResources is not null && selectedResources.Shader != selected.Description.Shader) selectedResources = null;
    }
    /// <summary>Executes explicit workgroup counts against the selected fixed executable.</summary>
    public void Dispatch(in RHIDispatch dispatch)
    {
        RequireIdleSubmission();
        RequireAdmittedPipeline(selectedComputePipeline);
        submitting = true;
        try { backend.Dispatch(selectedComputePipeline, dispatch, selectedResources); }
        finally { submitting = false; }
    }
    /// <summary>Executes GPU-produced arguments from a validated range without reading their contents.</summary>
    public void DispatchIndirect(in RHIBufferRange arguments)
    {
        RequireIdleSubmission();
        submitting = true;
        try { backend.DispatchIndirect(selectedComputePipeline, arguments, selectedResources); }
        finally { submitting = false; }
    }
    #endregion

    #region Private
    /// <summary>Rejects recursive submission and selection changes while native callbacks are active.</summary>
    private void RequireIdleSubmission()
    {
        if (submitting) throw new InvalidOperationException("RHI submission cannot recurse or mutate its selected pipeline.");
    }
    #endregion
}
