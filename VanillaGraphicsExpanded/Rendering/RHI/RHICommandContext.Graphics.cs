using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Coordinates immutable graphics values and geometry on the same immediate context as compute.</summary>
public sealed partial class RHICommandContext
{
    private RHIDynamicState selectedDynamics = new();

    #region Public API
    /// <summary>Retains immutable authored values without changing native state until a rendering command executes.</summary>
    public void SetDynamicState(RHIDynamicState values)
    {
        ArgumentNullException.ThrowIfNull(values);
        backend.RequireContext();
        RequireIdleSubmission();
        selectedDynamics = values;
    }
    /// <summary>Executes direct nonindexed geometry under the current pass, fixed pipeline and captured resources.</summary>
    public void Draw(RHIGeometryInputs geometry, in RHIDraw draw)
    {
        RequireIdleSubmission();
        submitting = true;
        try { backend.Draw(selectedGraphicsPipeline, geometry, draw, selectedDynamics, selectedResources); }
        finally { submitting = false; }
    }
    /// <summary>Executes explicit indexed geometry without interpreting its producer-owned index contents.</summary>
    public void DrawIndexed(RHIGeometryInputs geometry, in RHIDrawIndexed draw)
    {
        RequireIdleSubmission();
        submitting = true;
        try { backend.DrawIndexed(selectedGraphicsPipeline, geometry, draw, selectedDynamics, selectedResources); }
        finally { submitting = false; }
    }
    /// <summary>Consumes stable noninstanced draw intervals immediately; recorded callers copy the same supplied values.</summary>
    public void DrawIndexedBatch(RHIGeometryInputs geometry, ReadOnlySpan<RHIDrawIndexed> draws)
    {
        RequireIdleSubmission();
        submitting = true;
        try { backend.DrawIndexedBatch(selectedGraphicsPipeline, geometry, draws, selectedDynamics, selectedResources); }
        finally { submitting = false; }
    }
    /// <summary>Executes explicit indirect argument and optional count intervals without inspecting generated values.</summary>
    public void DrawIndirect(RHIGeometryInputs geometry, in RHIIndirectDraw draw)
    {
        RequireIdleSubmission();
        submitting = true;
        try { backend.DrawIndirect(selectedGraphicsPipeline, geometry, draw, selectedDynamics, selectedResources); }
        finally { submitting = false; }
    }
    /// <summary>Executes producer-authored indexed indirect packets within their declared buffer intervals.</summary>
    public void DrawIndexedIndirect(RHIGeometryInputs geometry, in RHIIndirectDraw draw)
    {
        RequireIdleSubmission();
        submitting = true;
        try { backend.DrawIndexedIndirect(selectedGraphicsPipeline, geometry, draw, selectedDynamics, selectedResources); }
        finally { submitting = false; }
    }
    #endregion
}
