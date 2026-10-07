using System;
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
        // Custom shader clipping is unsupported; never inherit the engine's enabled distances.
        for (int i = 0; i < caps.MaxClipDistances; i++) SetClipDistance(i, false);
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
