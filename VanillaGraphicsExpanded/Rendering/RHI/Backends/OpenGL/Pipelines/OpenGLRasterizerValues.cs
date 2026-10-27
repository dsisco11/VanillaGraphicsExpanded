using System;
using Native = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maps semantic raster policies to native state values without assuming token equivalence.</summary>
internal static class OpenGLRasterizerValues
{
    #region Internal API
    #region Face and polygon policy
    /// <summary>Translates the CullMode policy and rejects undefined semantic values.</summary>
    internal static Native.CullFaceMode CullMode(RasterizerCullMode value) => value switch
    {
        RasterizerCullMode.Front => Native.CullFaceMode.Front,
        RasterizerCullMode.Back => Native.CullFaceMode.Back,
        RasterizerCullMode.FrontAndBack => Native.CullFaceMode.FrontAndBack,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates the FrontFace policy and rejects undefined semantic values.</summary>
    internal static Native.FrontFaceDirection FrontFace(FrontFaceWinding value) => value switch
    {
        FrontFaceWinding.Clockwise => Native.FrontFaceDirection.Cw,
        FrontFaceWinding.CounterClockwise => Native.FrontFaceDirection.Ccw,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates the PolygonMode policy and rejects undefined semantic values.</summary>
    internal static Native.PolygonMode PolygonMode(RasterizerFillMode value) => value switch
    {
        RasterizerFillMode.Fill => Native.PolygonMode.Fill,
        RasterizerFillMode.Line => Native.PolygonMode.Line,
        RasterizerFillMode.Point => Native.PolygonMode.Point,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    #endregion
    #region Primitive and coordinate conventions
    /// <summary>Translates the ProvokingVertex policy and rejects undefined semantic values.</summary>
    internal static Native.ProvokingVertexMode ProvokingVertex(ProvokingVertexConvention value) => value switch
    {
        ProvokingVertexConvention.First => Native.ProvokingVertexMode.FirstVertexConvention,
        ProvokingVertexConvention.Last => Native.ProvokingVertexMode.LastVertexConvention,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates the ClipOrigin policy and rejects undefined semantic values.</summary>
    internal static Native.ClipOrigin ClipOrigin(ClipCoordinateOrigin value) => value switch
    {
        ClipCoordinateOrigin.LowerLeft => Native.ClipOrigin.LowerLeft,
        ClipCoordinateOrigin.UpperLeft => Native.ClipOrigin.UpperLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates the ClipDepth policy and rejects undefined semantic values.</summary>
    internal static Native.ClipDepthMode ClipDepth(ClipDepthRange value) => value switch
    {
        ClipDepthRange.NegativeOneToOne => Native.ClipDepthMode.NegativeOneToOne,
        ClipDepthRange.ZeroToOne => Native.ClipDepthMode.ZeroToOne,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Translates the PointSpriteOrigin policy and rejects undefined semantic values.</summary>
    internal static Native.PointSpriteCoordOriginParameter ToNativePointSpriteOrigin(PointSpriteOrigin value) => value switch
    {
        PointSpriteOrigin.LowerLeft => Native.PointSpriteCoordOriginParameter.LowerLeft,
        PointSpriteOrigin.UpperLeft => Native.PointSpriteCoordOriginParameter.UpperLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    #endregion
    #endregion
}
