using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects fixed graphics generations and publishes frozen resources through the unified execution context.</summary>
public sealed partial class RHICommandContext
{
    private RHIGraphicsPipelineRef selectedGraphicsPipeline;
    private RHIResourceBindings? selectedResources;

    #region Public API
    /// <summary>Selects one exact graphics realization without following its former asset owner's reloads.</summary>
    public void SetPipeline(RHIGraphicsPipelineRef pipeline)
    {
        backend.RequireContext(); RequireIdleSubmission();
        RequireAdmittedPipeline(pipeline);
        var selected = pipeline.RequirePipeline();
        selectedGraphicsPipeline = pipeline;
        selectedComputePipeline = default;
        if (selectedResources is not null && selectedResources.Shader != selected.Description.Shader) selectedResources = null;
    }
    /// <summary>Validates and publishes the complete captured table for the selected executable, committing only successful versions.</summary>
    public void BindResources(RHIResourceBindings inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        backend.RequireContext(); RequireIdleSubmission();
        RHIGraphicsPipeline? graphics = selectedGraphicsPipeline.IsEmpty ? null : selectedGraphicsPipeline.RequirePipeline();
        RHIComputePipeline? compute = selectedComputePipeline.IsEmpty ? null : selectedComputePipeline.RequirePipeline();
        if (graphics is null && compute is null) throw new InvalidOperationException("Select a pipeline before publishing resources.");
        var shader = graphics?.Description.Shader ?? compute!.Description.Shader;
        bool borrowed = false;
        submitting = true;
        try
        {
            if (graphics is not null) graphics.BeginSubmission(); else compute!.BeginSubmission();
            borrowed = true;
            backend.BindResources(shader, inputs);
            selectedResources = inputs;
        }
        catch { selectedResources = null; throw; }
        finally
        {
            if (borrowed) { if (graphics is not null) graphics.EndSubmission(); else compute!.EndSubmission(); }
            submitting = false;
        }
    }
    #endregion
}
