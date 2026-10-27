namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Executes graphics packets against fixed pipelines and the active validated render pass.</summary>
internal partial interface IRHIBackend
{
    #region Public API
    /// <summary>Executes direct vertex and instance intervals after complete packet validation.</summary>
    void Draw(RHIGraphicsPipelineRef pipeline, RHIGeometryInputs geometry, in RHIDraw draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources);
    /// <summary>Executes a bounded index interval without reading producer-owned index values.</summary>
    void DrawIndexed(RHIGraphicsPipelineRef pipeline, RHIGeometryInputs geometry, in RHIDrawIndexed draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources);
    /// <summary>Executes stable noninstanced index intervals as one native multi-draw without retaining caller memory.</summary>
    void DrawIndexedBatch(RHIGraphicsPipelineRef pipeline, RHIGeometryInputs geometry, System.ReadOnlySpan<RHIDrawIndexed> draws,
        RHIDynamicState dynamics, RHIResourceBindings? resources);
    /// <summary>Executes GPU-produced direct arguments without reading their contents.</summary>
    void DrawIndirect(RHIGraphicsPipelineRef pipeline, RHIGeometryInputs geometry, in RHIIndirectDraw draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources);
    /// <summary>Executes GPU-produced indexed arguments with an explicit admitted index stream.</summary>
    void DrawIndexedIndirect(RHIGraphicsPipelineRef pipeline, RHIGeometryInputs geometry, in RHIIndirectDraw draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources);
    #endregion
}
