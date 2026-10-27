namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines immediate pass admission and target realization under the existing engine restoration service.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Resolves only the declared pipeline/helper footprint before granting engine mutation authority.</summary>
    bool TryBeginBoundary(RHIBoundaryDescription description, out EngineBoundaryScope? boundary);
    /// <summary>Resolves a target compatibility signature from published attachment facts without framebuffer realization.</summary>
    Pipeline.Descriptions.RenderTargetSignature DescribeRenderTargets(RHIRenderPassDescription description);
    /// <summary>Realizes an attachment target and begins validated load operations without nesting passes.</summary>
    void BeginRenderPass(RHIRenderPassDescription description, EngineBoundaryScope boundary);
    /// <summary>Ends the active pass and releases attachment guards.</summary>
    void EndRenderPass();
    /// <summary>Gets whether the backend currently owns image/pass authority.</summary>
    bool HasRenderPass { get; }
    /// <summary>Gets the active pass's realized pixel viewport.</summary>
    RHIViewport PassViewport { get; }
    /// <summary>Withdraws the consumed pass token after specialized boundary cleanup or context abandonment.</summary>
    void ReleaseBoundaryPass();
    /// <summary>Copies selected pixel rectangles using named framebuffer transfer, preserving unrelated selectors and draw-state values.</summary>
    void BlitImages(in RHIImageBlit blit);
}
