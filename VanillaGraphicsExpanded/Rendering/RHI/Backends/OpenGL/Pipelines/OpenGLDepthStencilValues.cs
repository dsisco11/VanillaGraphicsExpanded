using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Translates semantic depth and stencil behavior to the existing native state-cache domains.</summary>
internal static class OpenGLDepthStencilValues
{
    #region Internal API
    /// <summary>Maps every depth comparison explicitly and rejects undefined semantic values.</summary>
    internal static DepthFunction DepthComparison(ComparisonFunction value) => value switch
    {
        ComparisonFunction.Never => DepthFunction.Never,
        ComparisonFunction.Less => DepthFunction.Less,
        ComparisonFunction.Equal => DepthFunction.Equal,
        ComparisonFunction.LessOrEqual => DepthFunction.Lequal,
        ComparisonFunction.Greater => DepthFunction.Greater,
        ComparisonFunction.NotEqual => DepthFunction.Notequal,
        ComparisonFunction.GreaterOrEqual => DepthFunction.Gequal,
        ComparisonFunction.Always => DepthFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Maps every stencil comparison without relying on native enum numbering.</summary>
    internal static StencilFunction StencilComparison(ComparisonFunction value) => value switch
    {
        ComparisonFunction.Never => StencilFunction.Never,
        ComparisonFunction.Less => StencilFunction.Less,
        ComparisonFunction.Equal => StencilFunction.Equal,
        ComparisonFunction.LessOrEqual => StencilFunction.Lequal,
        ComparisonFunction.Greater => StencilFunction.Greater,
        ComparisonFunction.NotEqual => StencilFunction.Notequal,
        ComparisonFunction.GreaterOrEqual => StencilFunction.Gequal,
        ComparisonFunction.Always => StencilFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Preserves clamping and wrapping distinctions when selecting native stencil updates.</summary>
    internal static StencilOp ToNativeStencilOperation(StencilOperation value) => value switch
    {
        StencilOperation.Keep => StencilOp.Keep,
        StencilOperation.Zero => StencilOp.Zero,
        StencilOperation.Replace => StencilOp.Replace,
        StencilOperation.IncrementClamp => StencilOp.Incr,
        StencilOperation.DecrementClamp => StencilOp.Decr,
        StencilOperation.Invert => StencilOp.Invert,
        StencilOperation.IncrementWrap => StencilOp.IncrWrap,
        StencilOperation.DecrementWrap => StencilOp.DecrWrap,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    #endregion
}
