using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Validates authored depth, stencil, raster and sampling settings before canonicalization.</summary>
internal static class PipelineFixedFunctionValidation
{
    #region Public API
    /// <summary>Rejects non-finite, unsupported and out-of-range state even when its category is disabled.</summary>
    public static void Validate(GraphicsPipelineDesc value, GraphicsCapabilities caps)
    {
        var depth = value.DepthStencil;
        Defined(depth.DepthComparison);
        ValidateStencil(depth.Front, depth.StencilTest);
        ValidateStencil(depth.Back, depth.StencilTest);
        var raster = value.Rasterizer;
        Defined(raster.CullMode); Defined(raster.FrontFace); Defined(raster.PolygonMode); Defined(raster.ProvokingVertex);
        Finite(raster.DepthBiasFactor); Finite(raster.DepthBiasUnits);
        if (raster.LineWidth <= 0 || raster.PointSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Line width and point size must be positive.");
        Range(raster.LineWidth, caps.MinLineWidth, caps.MaxLineWidth);
        Range(raster.PointSize, caps.MinPointSize, caps.MaxPointSize);
        if (raster.DepthClamp && !caps.DepthClamp) throw new NotSupportedException("Depth clamping is unavailable.");

        ValidateCompatibilityRaster(raster, caps);

        var sampling = value.Sampling;
        Range(sampling.Coverage, 0, 1); Range(sampling.MinimumSampleShading, 0, 1);
        if (sampling.Masks is { } masks && masks.Count > caps.MaxSampleMaskWords)
            throw new NotSupportedException("Too many sample-mask words.");
        if (sampling.SampleShading && !caps.SampleShading)
            throw new NotSupportedException("Sample shading is unavailable.");
    }
    /// <summary>Checks static raster settings independently of a linked executable.</summary>
    internal static void ValidateCompatibilityRaster(RasterizerDesc raster, GraphicsCapabilities caps)
    {
        Defined(raster.ClipOrigin); Defined(raster.ClipDepth); Defined(raster.PointSpriteOrigin);
        Defined(raster.AlphaComparison); Range(raster.AlphaReference, 0, 1);
        if (raster.LineStippleFactor < 1 || raster.LineStippleFactor > 256)
            throw new ArgumentOutOfRangeException(nameof(raster.LineStippleFactor));
        if (raster.PolygonStipplePattern is null || raster.PolygonStipplePattern.Count != 128)
            throw new ArgumentException("Polygon stipple requires exactly 128 bytes.");
        if (caps.MaxClipDistances < 0 || caps.MaxClipDistances > 32)
            throw new NotSupportedException("The clip-distance limit exceeds the supported mask width.");
        uint allowed = caps.MaxClipDistances == 32 ? uint.MaxValue : (1u << caps.MaxClipDistances) - 1;
        if ((raster.ClipDistances & ~allowed) != 0) throw new NotSupportedException("Clip-distance mask exceeds the implementation limit.");
        if (!caps.ClipControl && (raster.ClipOrigin != ClipOrigin.LowerLeft || raster.ClipDepth != ClipDepthMode.NegativeOneToOne))
            throw new NotSupportedException("Clip control is unavailable.");
        // Line and polygon smoothing remain core state; the other legacy tests/stipple controls were removed from core.
        if (caps.CoreProfile && (raster.AlphaTest || raster.PointSmooth || raster.LineStipple || raster.PolygonStipple))
            throw new NotSupportedException("Requested rasterization requires a compatibility profile.");
    }
    #endregion

    #region Private
    /// <summary>Validates both disabled authored faces and active attachment-width constraints.</summary>
    private static void ValidateStencil(StencilFaceDesc face, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(face);
        Defined(face.Comparison); Defined(face.Fail); Defined(face.DepthFail); Defined(face.Pass);
        // Every supported stencil target has eight bits. All-ones is the portable construction default.
        if (enabled && ((face.ReadMask > 255 && face.ReadMask != uint.MaxValue)
            || (face.WriteMask > 255 && face.WriteMask != uint.MaxValue)))
            throw new ArgumentException("Stencil mask exceeds the target's eight-bit aspect.");
    }
    /// <summary>Rejects undefined native enum values in all configurations.</summary>
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
