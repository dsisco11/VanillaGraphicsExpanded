using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Validates authored depth, stencil, raster and sampling settings before canonicalization.</summary>
internal static class PipelineFixedFunctionValidation
{
    #region Public API
    /// <summary>Rejects non-finite, unsupported and out-of-range state even when its category is disabled.</summary>
    public static void Validate(RHIGraphicsPipelineDescription value, GraphicsCapabilities caps)
    {
        ValidateShape(value);
        Range(value.Rasterizer.LineWidth, caps.MinLineWidth, caps.MaxLineWidth);
        Range(value.Rasterizer.PointSize, caps.MinPointSize, caps.MaxPointSize);
        if (value.Sampling.Masks is { } masks && masks.Count > caps.MaxSampleMaskWords)
            throw new NotSupportedException("Too many sample-mask words.");
        if (caps.MaxClipDistances < 0 || caps.MaxClipDistances > 32)
            throw new NotSupportedException("The clip-distance limit exceeds the supported mask width.");
    }
    /// <summary>Validates authored enums, scalar domains and stencil representation without device discovery.</summary>
    internal static void ValidateShape(RHIGraphicsPipelineDescription value)
    {
        var depth = value.DepthStencil;
        Defined(depth.DepthComparison);
        ValidateStencil(depth.Front); ValidateStencil(depth.Back);
        var raster = value.Rasterizer;
        Defined(raster.CullMode); Defined(raster.FrontFace); Defined(raster.PolygonMode); Defined(raster.ProvokingVertex);
        Defined(raster.ClipOrigin); Defined(raster.ClipDepth); Defined(raster.PointSpriteOrigin);
        Finite(raster.DepthBiasFactor); Finite(raster.DepthBiasUnits);
        Finite(raster.LineWidth); Finite(raster.PointSize);
        if (raster.LineWidth <= 0 || raster.PointSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Line width and point size must be positive.");
        Range(value.Sampling.Coverage, 0, 1);
        Range(value.Sampling.MinimumSampleShading, 0, 1);
    }
    /// <summary>Checks static raster settings independently of a linked executable.</summary>
    internal static void ValidateRaster(RasterizerDesc raster, GraphicsCapabilities caps)
    {
        Defined(raster.ClipOrigin); Defined(raster.ClipDepth); Defined(raster.PointSpriteOrigin);
        if (caps.MaxClipDistances < 0 || caps.MaxClipDistances > 32)
            throw new NotSupportedException("The clip-distance limit exceeds the supported mask width.");
    }
    #endregion

    #region Private
    /// <summary>Validates both active and dormant faces against the supported eight-bit representation.</summary>
    private static void ValidateStencil(StencilFaceDesc face)
    {
        ArgumentNullException.ThrowIfNull(face);
        Defined(face.Comparison); Defined(face.Fail); Defined(face.DepthFail); Defined(face.Pass);
        // Every supported stencil target has eight bits. All-ones is the portable construction default.
        if ((face.ReadMask > 255 && face.ReadMask != uint.MaxValue)
            || (face.WriteMask > 255 && face.WriteMask != uint.MaxValue))
            throw new ArgumentException("Stencil mask exceeds the target's eight-bit aspect.");
    }
    /// <summary>Rejects undefined semantic enum values in all configurations.</summary>
    private static void Defined<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Undefined pipeline enum.");
    }
    /// <summary>Rejects NaN and infinity rather than admitting unstable equality or invalid native state.</summary>
    private static void Finite(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
    }
    /// <summary>Checks exact authored bounds without silently clamping the reusable identity.</summary>
    private static void Range(float value, float minimum, float maximum)
    {
        Finite(value);
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum > maximum || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Pipeline value exceeds supported range.");
    }
    #endregion
}
