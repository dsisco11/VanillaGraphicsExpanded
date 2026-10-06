using System;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Applies the configurable raster portion of a complete description through shared transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes clipping and compatibility state; other complete pipeline categories retain their own application owners.</summary>
    internal void ApplyConfigurableRaster(GraphicsPipelineDesc pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var value = pipeline.Rasterizer;
        var caps = GpuSupport.Graphics;
        PipelineFixedFunctionValidation.ValidateCompatibilityRaster(value, caps);
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ConfigurableRaster);
        // Descriptions are reusable across compatible devices; validate the actual device before any native change.
        for (int i = 0; i < caps.MaxClipDistances; i++) SetClipDistance(i, (value.ClipDistances & (1u << i)) != 0);
        SetClipControl(value.ClipOrigin, value.ClipDepth);
        SetPointSpriteOrigin(value.PointSpriteOrigin);
        SetLineSmooth(value.LineSmooth);
        SetPolygonSmooth(value.PolygonSmooth);
        if (!caps.CoreProfile)
        {
            SetAlphaFunction(value.AlphaComparison, value.AlphaReference);
            SetAlphaTest(value.AlphaTest);
            SetPointSmooth(value.PointSmooth);
            SetLineStipple(value.LineStippleFactor, value.LineStipplePattern);
            SetLineStipple(value.LineStipple);
            SetPolygonStipplePattern(value.PolygonStipplePattern);
            SetPolygonStipple(value.PolygonStipple);
        }
    }
    #endregion
}
