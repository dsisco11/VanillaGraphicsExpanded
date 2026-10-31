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

        submitting = true;
        try
        {

            backend.BindResources(shader, inputs);
            selectedResources = inputs;
        }
        catch { selectedResources = null; throw; }
        finally
        {

            submitting = false;
        }
    }
    /// <summary>Publishes a captured uniform version without taking executable ownership.</summary>
    public bool BindUniformSnapshot(int slot, RHIUniformSnapshot snapshot)
    {
        backend.RequireContext(); RequireIdleSubmission();
        submitting = true;
        try { return backend.BindUniformSnapshot(slot, snapshot); }
        finally { submitting = false; }
    }
    /// <summary>Publishes a complete supplied set of sampled slots without owning the consuming executable.</summary>
    public void BindSampledTextures(RHISampledTextureBindings inputs)
    {
        backend.RequireContext(); RequireIdleSubmission();
        submitting = true;
        try { backend.BindSampledTextures(inputs); }
        finally { submitting = false; }
    }
    #endregion
}
