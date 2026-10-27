using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Applies routed blend groups without neutralizing dormant output slots.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Uses indexed groups or a compatible active global payload, covering every native alias.</summary>
    private void ApplyPipelineBlending(RHIGraphicsPipelineDescription pipeline, GraphicsStateRelevance relevance)
    {
        ColorBlendDesc? common = null;
        for (int slot = 0; slot < relevance.Blending.Count; slot++)
        {
            var groups = relevance.Blending[slot];
            var value = pipeline.Blending[slot];
            if (groups.HasFlag(BlendStateKnowledge.Enabled))
            {
                SetBlendEnabledIndexed(slot, value.Enabled);
                if (value.Enabled) common ??= value;
            }
            if (groups.HasFlag(BlendStateKnowledge.WriteMask))
                SetColorMaskIndexed(slot, GlColorMask.FromRgba(value.WriteRed, value.WriteGreen, value.WriteBlue, value.WriteAlpha));
            // Separate native calls still write complete RGB/alpha tuples, including dormant operands.
            if (groups.HasFlag(BlendStateKnowledge.Factors))
                SetBlendFuncIndexed(slot, OpenGLBlendValues.Factors(value));
            if (groups.HasFlag(BlendStateKnowledge.Equations)) SetBlendEquationIndexed(slot, OpenGLBlendValues.Equation(value.RgbEquation), OpenGLBlendValues.Equation(value.AlphaEquation));
        }
        // Validation guarantees compatible active payloads on contexts without independent blending.
        if (common is null) return;
        if (relevance.GlobalBlend.HasFlag(BlendStateKnowledge.Factors))
            SetBlendFunc(OpenGLBlendValues.Factors(common));
        if (relevance.GlobalBlend.HasFlag(BlendStateKnowledge.Equations)) SetBlendEquation(OpenGLBlendValues.Equation(common.RgbEquation), OpenGLBlendValues.Equation(common.AlphaEquation));
    }
    #endregion
}
