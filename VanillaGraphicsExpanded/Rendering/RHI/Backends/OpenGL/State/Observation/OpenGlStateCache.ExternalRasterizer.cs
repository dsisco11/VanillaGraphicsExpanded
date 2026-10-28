using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates defined external rasterizer effects into the existing executed-state authority.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    #region Clipping
    /// <summary>Commits the defined ClipControl effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalClipControl(ClipOrigin origin, ClipDepthMode depth)
    {
        rasterizer.ClipOrigin = origin; rasterizer.ClipDepth = depth; rasterizerKnown |= RasterizerStateKnowledge.ClipControl;
    }
    /// <summary>Commits the defined PointParameter effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalPointParameter(PointParameterName parameter, int value)
    {
        rasterizer.PointSpriteOrigin = (PointSpriteCoordOriginParameter)value; rasterizerKnown |= RasterizerStateKnowledge.PointSpriteOrigin;
    }
    #endregion
    #region Raster parameters
    /// <summary>Commits the defined LineWidth effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalLineWidth(float width)
    {
        rasterizer.LineWidth = width; rasterizerKnown |= RasterizerStateKnowledge.LineWidth;
    }
    /// <summary>Commits the defined PointSize effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalPointSize(float size)
    {
        rasterizer.PointSize = size; rasterizerKnown |= RasterizerStateKnowledge.PointSize;
    }
    /// <summary>Commits the defined ProvokingVertex effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalProvokingVertex(ProvokingVertexMode mode)
    {
        rasterizer.ProvokingVertex = mode; rasterizerKnown |= RasterizerStateKnowledge.ProvokingVertex;
    }
    /// <summary>Commits the defined CullFace effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalCullFace(CullFaceMode mode)
    {
        rasterizer.CullMode = (CullFaceMode)mode; rasterizerKnown |= RasterizerStateKnowledge.CullMode;
    }
    /// <summary>Commits the defined FrontFace effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalFrontFace(FrontFaceDirection direction)
    {
        rasterizer.FrontFace = direction; rasterizerKnown |= RasterizerStateKnowledge.FrontFace;
    }
    /// <summary>Commits the defined PolygonOffset effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalPolygonOffset(float factor, float units)
    {
        rasterizer.PolygonOffset = (factor, units); rasterizerKnown |= RasterizerStateKnowledge.PolygonOffset;
    }
    /// <summary>Commits the defined PolygonMode effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalPolygonMode(MaterialFace face, PolygonMode mode)
    {
        rasterizer.PolygonModes = mode; rasterizerKnown |= RasterizerStateKnowledge.PolygonModes;
    }
    /// <summary>Commits the defined PolygonMode effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalPolygonMode(TriangleFace face, PolygonMode mode)
    {
        rasterizer.PolygonModes = mode; rasterizerKnown |= RasterizerStateKnowledge.PolygonModes;
    }
    /// <summary>Commits the defined CullFace effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalCullFace(TriangleFace mode)
    {
        rasterizer.CullMode = (CullFaceMode)mode; rasterizerKnown |= RasterizerStateKnowledge.CullMode;
    }
    #endregion
    #endregion
}
