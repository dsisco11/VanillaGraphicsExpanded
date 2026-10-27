using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Translates semantic blend descriptions without relying on native token numbering.</summary>
internal static class OpenGLBlendValues
{
    #region Internal API
    /// <summary>Translates the complete RGB and alpha factor tuple retained by the native state cache.</summary>
    internal static GlBlendFunc Factors(ColorBlendDesc value) => new(SourceFactor(value.SourceRgb), DestinationFactor(value.DestinationRgb),
        SourceFactor(value.SourceAlpha), DestinationFactor(value.DestinationAlpha));
    /// <summary>Maps supported semantic blend equations explicitly.</summary>
    internal static BlendEquationMode Equation(BlendOperation value) => value switch
    {
        BlendOperation.Add => BlendEquationMode.FuncAdd,
        BlendOperation.Subtract => BlendEquationMode.FuncSubtract,
        BlendOperation.ReverseSubtract => BlendEquationMode.FuncReverseSubtract,
        BlendOperation.Minimum => BlendEquationMode.Min,
        BlendOperation.Maximum => BlendEquationMode.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Maps a source multiplier, retaining the source-only saturation operation.</summary>
    internal static BlendingFactorSrc SourceFactor(BlendFactor value) => value switch
    {
        BlendFactor.Zero => BlendingFactorSrc.Zero,
        BlendFactor.One => BlendingFactorSrc.One,
        BlendFactor.SourceColor => BlendingFactorSrc.SrcColor,
        BlendFactor.OneMinusSourceColor => BlendingFactorSrc.OneMinusSrcColor,
        BlendFactor.SourceAlpha => BlendingFactorSrc.SrcAlpha,
        BlendFactor.OneMinusSourceAlpha => BlendingFactorSrc.OneMinusSrcAlpha,
        BlendFactor.DestinationColor => BlendingFactorSrc.DstColor,
        BlendFactor.OneMinusDestinationColor => BlendingFactorSrc.OneMinusDstColor,
        BlendFactor.DestinationAlpha => BlendingFactorSrc.DstAlpha,
        BlendFactor.OneMinusDestinationAlpha => BlendingFactorSrc.OneMinusDstAlpha,
        BlendFactor.ConstantColor => BlendingFactorSrc.ConstantColor,
        BlendFactor.OneMinusConstantColor => BlendingFactorSrc.OneMinusConstantColor,
        BlendFactor.ConstantAlpha => BlendingFactorSrc.ConstantAlpha,
        BlendFactor.OneMinusConstantAlpha => BlendingFactorSrc.OneMinusConstantAlpha,
        BlendFactor.SourceAlphaSaturate => BlendingFactorSrc.SrcAlphaSaturate,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Maps a destination multiplier while rejecting source-only saturation.</summary>
    internal static BlendingFactorDest DestinationFactor(BlendFactor value) => value switch
    {
        BlendFactor.Zero => BlendingFactorDest.Zero,
        BlendFactor.One => BlendingFactorDest.One,
        BlendFactor.SourceColor => BlendingFactorDest.SrcColor,
        BlendFactor.OneMinusSourceColor => BlendingFactorDest.OneMinusSrcColor,
        BlendFactor.SourceAlpha => BlendingFactorDest.SrcAlpha,
        BlendFactor.OneMinusSourceAlpha => BlendingFactorDest.OneMinusSrcAlpha,
        BlendFactor.DestinationColor => BlendingFactorDest.DstColor,
        BlendFactor.OneMinusDestinationColor => BlendingFactorDest.OneMinusDstColor,
        BlendFactor.DestinationAlpha => BlendingFactorDest.DstAlpha,
        BlendFactor.OneMinusDestinationAlpha => BlendingFactorDest.OneMinusDstAlpha,
        BlendFactor.ConstantColor => BlendingFactorDest.ConstantColor,
        BlendFactor.OneMinusConstantColor => BlendingFactorDest.OneMinusConstantColor,
        BlendFactor.ConstantAlpha => BlendingFactorDest.ConstantAlpha,
        BlendFactor.OneMinusConstantAlpha => BlendingFactorDest.OneMinusConstantAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    #endregion
}
