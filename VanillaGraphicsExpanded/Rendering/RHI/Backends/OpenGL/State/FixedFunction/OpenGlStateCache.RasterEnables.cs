using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns cached transitions for configurable raster state.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes LineSmooth, retaining unknown knowledge when optional diagnostics detect native failure.</summary>
    internal void SetLineSmooth(bool value)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.LineSmooth);
        
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineSmooth) && rasterizer.LineSmooth == value) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.LineSmooth;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        SetEnable(EnableCap.LineSmooth, value);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.LineSmooth = value;
        rasterizerKnown |= RasterizerStateKnowledge.LineSmooth;
    }
    /// <summary>Establishes PolygonSmooth, retaining unknown knowledge when optional diagnostics detect native failure.</summary>
    internal void SetPolygonSmooth(bool value)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonSmooth);
        
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonSmooth) && rasterizer.PolygonSmooth == value) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonSmooth;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        SetEnable(EnableCap.PolygonSmooth, value);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonSmooth = value;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonSmooth;
    }
    #endregion
}
