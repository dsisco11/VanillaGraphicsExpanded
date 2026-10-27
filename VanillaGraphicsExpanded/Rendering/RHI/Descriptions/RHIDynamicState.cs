namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Freezes explicitly supplied draw values; missing pipeline-required values are rejected before execution.</summary>
public sealed record RHIDynamicState
{
    #region Public API
    /// <summary>Gets the viewport supplied for the selected pass.</summary>
    public RHIViewport? Viewport { get; init; }
    /// <summary>Gets an optional scissor rectangle independently of scissor-test enable state.</summary>
    public (int X, int Y, int Width, int Height)? Scissor { get; init; }
    /// <summary>Gets optional front and back stencil reference values.</summary>
    public (int Front, int Back)? StencilReference { get; init; }
    /// <summary>Gets the optional floating-point blend constant.</summary>
    public (float R, float G, float B, float A)? BlendConstant { get; init; }
    #endregion

    #region Internal API
    /// <summary>Checks complete required values without native state access, preserving declared fields even when dormant.</summary>
    internal void ValidateFor(RHIGraphicsPipelineDescription pipeline)
    {
        if (Viewport is not { } viewport || viewport.Width < 0 || viewport.Height < 0)
            throw new System.ArgumentException("A valid viewport is required.");
        if (pipeline.Dynamics.HasFlag(Pipeline.Descriptions.DynamicPipelineState.Scissor)
            && (Scissor is not { } scissor || scissor.Width < 0 || scissor.Height < 0))
            throw new System.ArgumentException("A valid scissor rectangle is required.");
        if (pipeline.Dynamics.HasFlag(Pipeline.Descriptions.DynamicPipelineState.StencilReference)
            && (StencilReference is not { } reference || reference.Front < 0 || reference.Front > 255 || reference.Back < 0 || reference.Back > 255))
            throw new System.ArgumentException("Front and back stencil references are required.");
        if (pipeline.Dynamics.HasFlag(Pipeline.Descriptions.DynamicPipelineState.BlendConstant)
            && (BlendConstant is not { } blend || !float.IsFinite(blend.R) || !float.IsFinite(blend.G)
                || !float.IsFinite(blend.B) || !float.IsFinite(blend.A)))
            throw new System.ArgumentException("A finite blend constant is required.");
    }
    #endregion
}
