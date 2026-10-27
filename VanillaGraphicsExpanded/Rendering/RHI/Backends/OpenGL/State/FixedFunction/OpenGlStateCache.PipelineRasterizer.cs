using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Applies the configurable raster portion of a complete description through shared transitions.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Disables custom clip distances and establishes origin/depth conventions.</summary>
    internal void ApplyConfigurableRaster(RHIGraphicsPipelineDescription pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var value = pipeline.Rasterizer;
        var caps = GpuSupport.Graphics;
        PipelineFixedFunctionValidation.ValidateRaster(value, caps);
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ConfigurableRaster);
        ApplyConfigurableRaster(value, RasterizerStateKnowledge.ConfigurableRaster);
    }
    #endregion

    #region Private
    /// <summary>Establishes only raster groups that can affect the prepared emitted primitive families.</summary>
    private void ApplyPipelineRasterizer(RHIGraphicsPipelineDescription pipeline, RasterizerStateKnowledge relevant)
    {
        var value = pipeline.Rasterizer;
        if (relevant.HasFlag(RasterizerStateKnowledge.CullEnabled)) SetCapability(EnableCap.CullFace, value.Cull);
        if (relevant.HasFlag(RasterizerStateKnowledge.CullMode)) SetCullMode(OpenGLRasterizerValues.CullMode(value.CullMode));
        if (relevant.HasFlag(RasterizerStateKnowledge.FrontFace)) SetFrontFace(OpenGLRasterizerValues.FrontFace(value.FrontFace));
        if (relevant.HasFlag(RasterizerStateKnowledge.PolygonModes)) SetPolygonMode(OpenGLRasterizerValues.PolygonMode(value.PolygonMode));
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
        if (relevant.HasFlag(RasterizerStateKnowledge.ProvokingVertex)) SetProvokingVertex(OpenGLRasterizerValues.ProvokingVertex(value.ProvokingVertex));
        ApplyConfigurableRaster(value, relevant);
    }

    /// <summary>Shares configurable native setters between explicit helpers and relevance-aware complete draws.</summary>
    private void ApplyConfigurableRaster(RasterizerDesc value, RasterizerStateKnowledge relevant)
    {
        var caps = GpuSupport.Graphics;
        // Custom shader clipping is unsupported; never inherit the engine's enabled distances.
        if (relevant.HasFlag(RasterizerStateKnowledge.ClipDistances))
            for (int i = 0; i < caps.MaxClipDistances; i++) SetClipDistance(i, false);
        if (relevant.HasFlag(RasterizerStateKnowledge.ClipControl)) SetClipControl(OpenGLRasterizerValues.ClipOrigin(value.ClipOrigin), OpenGLRasterizerValues.ClipDepth(value.ClipDepth));
        if (relevant.HasFlag(RasterizerStateKnowledge.PointSpriteOrigin)) SetPointSpriteOrigin(OpenGLRasterizerValues.ToNativePointSpriteOrigin(value.PointSpriteOrigin));
        if (relevant.HasFlag(RasterizerStateKnowledge.LineSmooth)) SetLineSmooth(value.LineSmooth);
        if (relevant.HasFlag(RasterizerStateKnowledge.PolygonSmooth)) SetPolygonSmooth(value.PolygonSmooth);
    }
    #endregion
}
