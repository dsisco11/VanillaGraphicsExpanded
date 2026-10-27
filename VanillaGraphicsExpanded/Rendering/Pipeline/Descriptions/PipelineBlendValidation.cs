using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Validates output-slot blending without confusing routing with attachment identity.</summary>
internal static class PipelineBlendValidation
{
    #region Public API
    /// <summary>Checks factors, dynamics, integer targets and independent-output support.</summary>
    public static void Validate(RHIGraphicsPipelineDescription value)
    {
        for (int slot = 0; slot < value.Blending.Count; slot++)
        {
            var blend = value.Blending[slot];
            Equation(blend.RgbEquation); Equation(blend.AlphaEquation);
            Factor(blend.SourceRgb, true); Factor(blend.DestinationRgb, false);
            Factor(blend.SourceAlpha, false); Factor(blend.DestinationAlpha, false);
            if (blend.Enabled && value.Targets.Colors[slot].Format is { } format && TargetFormatPolicy.IsInteger(format))
                throw new ArgumentException("Integer color targets cannot enable blending.");
            bool constant = blend.Enabled &&
                (PipelineBlendSemantics.UsesFactors(blend.RgbEquation) && (PipelineBlendSemantics.UsesConstant(blend.SourceRgb) || PipelineBlendSemantics.UsesConstant(blend.DestinationRgb))
                || PipelineBlendSemantics.UsesFactors(blend.AlphaEquation) && (PipelineBlendSemantics.UsesConstant(blend.SourceAlpha) || PipelineBlendSemantics.UsesConstant(blend.DestinationAlpha)));
            if (constant && !value.Dynamics.HasFlag(DynamicPipelineState.BlendConstant))
                throw new ArgumentException("Constant blend factors require declared blend-constant dynamics.");
        }
        if (value.Output.FramebufferSrgb && !value.Targets.Colors.Any(s => s.Format is TextureFormat.Srgb8 or TextureFormat.Srgb8Alpha8))
            throw new ArgumentException("Framebuffer sRGB requires an sRGB target.");
    }
    #endregion

    #region Private
    /// <summary>Restricts equations to the semantic blend operations supported by the pipeline contract.</summary>
    private static void Equation(BlendOperation value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
    }
    /// <summary>Rejects undefined factors and source-only saturation in unsupported operand positions.</summary>
    private static void Factor(BlendFactor value, bool allowSaturate)
    {
        if (!Enum.IsDefined(value) || !allowSaturate && value == BlendFactor.SourceAlphaSaturate)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported blend factor.");
    }
    #endregion
}
