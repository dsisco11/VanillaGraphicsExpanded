using System;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Admits complete immediate footprints before native mutation and rejects widening after entry.</summary>
public sealed partial class RHICommandContext
{
    private RHIBoundaryScope? activeBoundary;

    #region Public API
    /// <summary>Transfers explicit image rectangles, including permitted conversion and resolve, without changing pass selection.</summary>
    public void BlitImages(in RHIImageBlit blit)
    {
        backend.RequireContext(); RequireIdleSubmission();
        if (activeBoundary is not null && !activeBoundary.Description.Operations.HasFlag(BoundaryOperations.RenderPasses))
            throw new InvalidOperationException("Image-blit state effects were not declared before entry.");
        submitting = true;
        try { backend.BlitImages(blit); }
        finally { submitting = false; }
    }
    /// <summary>Reads published attachment compatibility for pipeline creation without realizing targets or acquiring pass guards.</summary>
    public Pipeline.Descriptions.RenderTargetSignature DescribeRenderTargets(RHIRenderPassDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        backend.RequireContext();
        return backend.DescribeRenderTargets(description);
    }
    /// <summary>Returns no scope when optional engine state cannot be resolved; no rendering effects have occurred on failure.</summary>
    public bool TryEnterBoundary(RHIBoundaryDescription description, out RHIBoundaryScope? scope)
    {
        ArgumentNullException.ThrowIfNull(description);
        backend.RequireContext(); RequireIdleSubmission();
        if (activeBoundary is not null) throw new InvalidOperationException("RHI engine boundaries cannot nest.");
        scope = null;
        if (!backend.TryBeginBoundary(description, out var native)) return false;
        scope = new(this, description, native!, (selectedGraphicsPipeline, selectedComputePipeline, selectedResources, selectedDynamics));
        activeBoundary = scope;
        selectedGraphicsPipeline = default; selectedComputePipeline = default; selectedResources = null; selectedDynamics = new();
        return true;
    }
    /// <summary>Enters required engine restoration authority or reports unavailable admission before rendering.</summary>
    public RHIBoundaryScope EnterBoundary(RHIBoundaryDescription description)
    {
        if (!TryEnterBoundary(description, out var scope)) throw new InvalidOperationException("The declared RHI boundary could not be admitted.");
        return scope!;
    }
    /// <summary>Realizes and begins a nonnested pass only when its helper effects were admitted before entry.</summary>
    public void BeginRenderPass(RHIRenderPassDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        backend.RequireContext(); RequireIdleSubmission();
        var boundary = activeBoundary ?? throw new InvalidOperationException("Begin a declared boundary before a render pass.");
        if (!boundary.Description.Operations.HasFlag(BoundaryOperations.RenderPasses))
            throw new InvalidOperationException("Render-pass effects were not declared at boundary entry.");
        submitting = true;
        try { backend.BeginRenderPass(description, boundary.Native); }
        finally { submitting = false; }
        selectedGraphicsPipeline = default; selectedComputePipeline = default; selectedResources = null; selectedDynamics = new();
    }
    /// <summary>Reports whether an immediate render pass currently owns attachment authority.</summary>
    public bool HasRenderPass => backend.HasRenderPass;
    /// <summary>Gets an explicit viewport from the active pass's realized area.</summary>
    public RHIViewport PassViewport { get { backend.RequireContext(); return backend.PassViewport; } }
    /// <summary>Ends image authority while retaining the admitted surrounding-state snapshot until boundary completion.</summary>
    public void EndRenderPass()
    {
        backend.RequireContext(); RequireIdleSubmission();
        submitting = true;
        try { backend.EndRenderPass(); }
        finally
        {
            submitting = false;
            selectedGraphicsPipeline = default; selectedComputePipeline = default; selectedResources = null; selectedDynamics = new();
        }
    }
    #endregion

    #region Internal API
    /// <summary>Rejects escaped, recursive or wrong-context boundary cleanup before consuming its native owner.</summary>
    internal void RequireBoundaryCleanup(RHIBoundaryScope scope)
    {
        backend.RequireContext(); RequireIdleSubmission();
        if (!ReferenceEquals(activeBoundary, scope)) throw new InvalidOperationException("The originating RHI boundary is not active.");
    }
    /// <summary>Restores pure logical selections after specialized native cleanup; it issues no shader activation or input replay.</summary>
    internal void ReleaseBoundary(RHIBoundaryScope scope)
    {
        backend.ReleaseBoundaryPass();
        (selectedGraphicsPipeline, selectedComputePipeline, selectedResources, selectedDynamics) = scope.Previous;
        activeBoundary = null;
    }
    #endregion

    #region Private
    /// <summary>Rejects a graphics realization not present in the immutable admitted set before binding or drawing.</summary>
    private void RequireAdmittedPipeline(RHIGraphicsPipelineRef pipeline)
    {
        if (activeBoundary is not null && !activeBoundary.Description.Graphics.Contains(pipeline))
            throw new InvalidOperationException("The graphics pipeline was not declared at boundary entry.");
    }
    /// <summary>Rejects undeclared compute work and any attempt to dispatch while a graphics pass owns attachments.</summary>
    private void RequireAdmittedPipeline(RHIComputePipelineRef pipeline)
    {
        if (backend.HasRenderPass) throw new InvalidOperationException("End the render pass before compute selection or dispatch.");
        if (activeBoundary is not null && !activeBoundary.Description.Compute.Contains(pipeline))
            throw new InvalidOperationException("The compute pipeline was not declared at boundary entry.");
    }
    #endregion
}
