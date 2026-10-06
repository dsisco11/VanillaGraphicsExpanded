using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns categorized supplemental state shared directly with detached boundary snapshots.</summary>
internal sealed partial class StateCache
{
    private CompleteSamplingState completeSampling;
    private CompleteStencilState completeStencil;
    private CompleteOutputState completeOutput;
    private readonly System.Collections.Generic.Dictionary<int, uint> completeSampleMasks = new();
    #region Public API
    #region Invalidation and equation knowledge
    /// <summary>Forgets supplemental knowledge without touching established resource binding caches.</summary>
    internal void InvalidateSupplementalGraphics(EPipelineState states)
    {
        if (states.HasFlag(EPipelineState.Depth) || states.HasFlag(EPipelineState.DepthRange)) depth.SupplementalKnown = default;
        if (states.HasFlag(EPipelineState.Blend)) ForgetBlendEquations();
        if (states.HasFlag(EPipelineState.RasterParameters)) { rasterizer.SupplementalKnown = default; rasterizer.SupplementalEnables = default; }
        if (states.HasFlag(EPipelineState.Stencil)) completeStencil = default;
        if (states.HasFlag(EPipelineState.Sampling)) { completeSampling = default; completeSampleMasks.Clear(); }
        if (states.HasFlag(EPipelineState.OutputInterpretation)) completeOutput = default;
        if (states.HasFlag(EPipelineState.PrimitiveRestart)) { assembly.SupplementalKnown = default; assembly.SupplementalEnables = default; }
        if (states.HasFlag(EPipelineState.ScissorRectangle)) dynamicState.SupplementalKnown &= ~CompleteDynamicKnowledge.Scissor;
        if (states.HasFlag(EPipelineState.BlendConstant)) dynamicState.SupplementalKnown &= ~CompleteDynamicKnowledge.BlendConstant;
    }
    /// <summary>Reads independently valid equation knowledge without querying native state.</summary>
    internal bool TryGetCachedBlendEquation(int index, out (BlendEquationMode Rgb, BlendEquationMode Alpha) equations)
    {
        equations = default;
        if (index < 0 || index >= blend.Length || !blendKnown[index].HasFlag(BlendStateKnowledge.Equations)) return false;
        equations = blend[index].Equations; return true;
    }
    /// <summary>Withdraws one equation pair after an uncertain native transition.</summary>
    internal void ForgetBlendEquation(int index)
    {
        if (index >= 0 && index < blendKnown.Length) blendKnown[index] &= ~BlendStateKnowledge.Equations;
    }
    /// <summary>Withdraws the equation alias group after an external global mutation.</summary>
    internal void ForgetBlendEquations()
    {
        for (int i = 0; i < blendKnown.Length; i++) blendKnown[i] &= ~BlendStateKnowledge.Equations;
    }
    #endregion
    #endregion
}
