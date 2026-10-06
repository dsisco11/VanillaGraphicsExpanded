using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Validates output-slot blending without confusing routing with attachment identity.</summary>
internal static class PipelineBlendValidation
{
    #region Public API
    /// <summary>Checks factors, dynamics, integer targets and independent-output support.</summary>
    public static void Validate(GraphicsPipelineDesc value, GraphicsCapabilities caps)
    {
        for (int slot = 0; slot < value.Blending.Count; slot++)
        {
            var blend = value.Blending[slot];
            Equation(blend.RgbEquation); Equation(blend.AlphaEquation);
            Factor((int)blend.SourceRgb, true); Factor((int)blend.DestinationRgb, false);
            Factor((int)blend.SourceAlpha, false); Factor((int)blend.DestinationAlpha, false);
            if (blend.Enabled && value.Targets.Colors[slot].Format is { } format && TargetFormatPolicy.IsInteger(format))
                throw new ArgumentException("Integer color targets cannot enable blending.");
            bool constant = blend.Enabled &&
                (PipelineCanonicalization.UsesFactors(blend.RgbEquation) && (Constant((int)blend.SourceRgb) || Constant((int)blend.DestinationRgb))
                || PipelineCanonicalization.UsesFactors(blend.AlphaEquation) && (Constant((int)blend.SourceAlpha) || Constant((int)blend.DestinationAlpha)));
            if (constant && !value.Dynamics.HasFlag(DynamicPipelineState.BlendConstant))
                throw new ArgumentException("Constant blend factors require declared blend-constant dynamics.");
        }
        // Pre-ARB_draw_buffers_blend supports indexed enables/masks, but equations/factors remain global.
        var active = value.Blending.Where(b => b.Enabled).Select(PipelineCanonicalization.Blend).ToArray();
        if (!caps.IndependentBlend && active.Select(b => (b.RgbEquation, b.AlphaEquation, b.SourceRgb,
            b.DestinationRgb, b.SourceAlpha, b.DestinationAlpha)).Distinct().Count() > 1)
            throw new NotSupportedException("Independent output equations/factors are unavailable.");
        if (value.Output.FramebufferSrgb && !value.Targets.Colors.Any(s => s.Format is PixelInternalFormat.Srgb8 or PixelInternalFormat.Srgb8Alpha8))
            throw new ArgumentException("Framebuffer sRGB requires an sRGB target.");
    }
    #endregion

    #region Private
    /// <summary>Restricts equations to the supported core blend operations.</summary>
    private static void Equation(BlendEquationMode value)
    {
        if (value is not (BlendEquationMode.FuncAdd or BlendEquationMode.FuncSubtract or BlendEquationMode.FuncReverseSubtract
            or BlendEquationMode.Min or BlendEquationMode.Max)) throw new ArgumentOutOfRangeException(nameof(value));
    }
    /// <summary>Rejects dual-source and extension factors until their shader-output contract is supported.</summary>
    private static void Factor(int value, bool allowSaturate)
    {
        // Source and destination factors use separate OpenTK enum types but share native token values.
        if (value is not ((int)BlendingFactorSrc.Zero or (int)BlendingFactorSrc.One
            or (int)BlendingFactorSrc.SrcColor or (int)BlendingFactorSrc.OneMinusSrcColor
            or (int)BlendingFactorSrc.SrcAlpha or (int)BlendingFactorSrc.OneMinusSrcAlpha
            or (int)BlendingFactorSrc.DstAlpha or (int)BlendingFactorSrc.OneMinusDstAlpha
            or (int)BlendingFactorSrc.DstColor or (int)BlendingFactorSrc.OneMinusDstColor
            or (int)BlendingFactorSrc.ConstantColor or (int)BlendingFactorSrc.OneMinusConstantColor
            or (int)BlendingFactorSrc.ConstantAlpha or (int)BlendingFactorSrc.OneMinusConstantAlpha)
            && !(allowSaturate && value == (int)BlendingFactorSrc.SrcAlphaSaturate))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported blend factor.");
    }
    /// <summary>Identifies the four core constant-color/alpha factors.</summary>
    private static bool Constant(int value) => value is
        (int)BlendingFactorSrc.ConstantColor or (int)BlendingFactorSrc.OneMinusConstantColor
        or (int)BlendingFactorSrc.ConstantAlpha or (int)BlendingFactorSrc.OneMinusConstantAlpha;
    #endregion
}
