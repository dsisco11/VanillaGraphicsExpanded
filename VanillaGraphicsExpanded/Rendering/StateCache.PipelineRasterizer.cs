using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Applies the configurable raster portion of a complete description through shared transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Disables custom clip distances and establishes origin/depth conventions and compatibility state.</summary>
    internal void ApplyConfigurableRaster(GraphicsPipelineDesc pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var value = pipeline.Rasterizer;
        var caps = GpuSupport.Graphics;
        PipelineFixedFunctionValidation.ValidateCompatibilityRaster(value, caps);
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ConfigurableRaster);
        ApplyConfigurableRaster(value, RasterizerStateKnowledge.ConfigurableRaster);
    }
    #endregion

    #region Private
    /// <summary>Establishes only raster groups that can affect the prepared emitted primitive families.</summary>
    private void ApplyPipelineRasterizer(GraphicsPipelineDesc pipeline, RasterizerStateKnowledge relevant)
    {
        var value = pipeline.Rasterizer;
        if (relevant.HasFlag(RasterizerStateKnowledge.CullEnabled)) SetCapability(EnableCap.CullFace, value.Cull);
        if (relevant.HasFlag(RasterizerStateKnowledge.CullMode)) SetCullMode(value.CullMode);
        if (relevant.HasFlag(RasterizerStateKnowledge.FrontFace)) SetFrontFace(value.FrontFace);
        if (relevant.HasFlag(RasterizerStateKnowledge.PolygonModes)) SetPolygonModes(value.PolygonMode, value.PolygonMode);
        if (relevant.HasFlag(RasterizerStateKnowledge.PolygonOffset)) SetPolygonOffset(value.DepthBiasFactor, value.DepthBiasUnits);
        if (relevant.HasFlag(RasterizerStateKnowledge.PolygonOffsetFill)) SetPolygonOffsetFillEnabled(value.OffsetFill);
        if (relevant.HasFlag(RasterizerStateKnowledge.PolygonOffsetLine)) SetPolygonOffsetLineEnabled(value.OffsetLine);
        if (relevant.HasFlag(RasterizerStateKnowledge.PolygonOffsetPoint)) SetPolygonOffsetPointEnabled(value.OffsetPoint);
        if (relevant.HasFlag(RasterizerStateKnowledge.DepthClamp)) SetDepthClampEnabled(value.DepthClamp);
        if (relevant.HasFlag(RasterizerStateKnowledge.RasterizerDiscard)) SetRasterizerDiscardEnabled(value.Discard);
        if (relevant.HasFlag(RasterizerStateKnowledge.ProgramPointSize)) SetProgramPointSizeEnabled(value.ProgramPointSize);
        if (relevant.HasFlag(RasterizerStateKnowledge.ScissorEnabled)) SetCapability(EnableCap.ScissorTest, value.Scissor);
        if (relevant.HasFlag(RasterizerStateKnowledge.LineWidth)) SetLineWidth(value.LineWidth);
        if (relevant.HasFlag(RasterizerStateKnowledge.PointSize)) SetPointSize(value.PointSize);
        if (relevant.HasFlag(RasterizerStateKnowledge.ProvokingVertex)) SetProvokingVertex(value.ProvokingVertex);
        ApplyConfigurableRaster(value, relevant);
    }

    /// <summary>Shares configurable native setters between explicit helpers and relevance-aware complete draws.</summary>
    private void ApplyConfigurableRaster(RasterizerDesc value, RasterizerStateKnowledge relevant)
    {
        var caps = GpuSupport.Graphics;
        // Custom shader clipping is unsupported; never inherit the engine's enabled distances.
        if (relevant.HasFlag(RasterizerStateKnowledge.ClipDistances))
            for (int i = 0; i < caps.MaxClipDistances; i++) SetClipDistance(i, false);
        if (relevant.HasFlag(RasterizerStateKnowledge.ClipControl)) SetClipControl(value.ClipOrigin, value.ClipDepth);
        if (relevant.HasFlag(RasterizerStateKnowledge.PointSpriteOrigin)) SetPointSpriteOrigin(value.PointSpriteOrigin);
        if (relevant.HasFlag(RasterizerStateKnowledge.LineSmooth)) SetLineSmooth(value.LineSmooth);
        if (relevant.HasFlag(RasterizerStateKnowledge.PolygonSmooth)) SetPolygonSmooth(value.PolygonSmooth);
        if (!caps.CoreProfile)
        {
            if (relevant.HasFlag(RasterizerStateKnowledge.AlphaFunction)) SetAlphaFunction(value.AlphaComparison, value.AlphaReference);
            if (relevant.HasFlag(RasterizerStateKnowledge.AlphaTest)) SetAlphaTest(value.AlphaTest);
            if (relevant.HasFlag(RasterizerStateKnowledge.PointSmooth)) SetPointSmooth(value.PointSmooth);
            if (relevant.HasFlag(RasterizerStateKnowledge.LineStippleParameters)) SetLineStipple(value.LineStippleFactor, value.LineStipplePattern);
            if (relevant.HasFlag(RasterizerStateKnowledge.LineStipple)) SetLineStipple(value.LineStipple);
            if (relevant.HasFlag(RasterizerStateKnowledge.PolygonStipplePattern)) SetPolygonStipplePattern(value.PolygonStipplePattern);
            if (relevant.HasFlag(RasterizerStateKnowledge.PolygonStipple)) SetPolygonStipple(value.PolygonStipple);
        }
    }
    #endregion
}
