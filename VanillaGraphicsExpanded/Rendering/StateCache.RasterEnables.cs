using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns cached transitions for configurable clipping and compatibility raster state.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes AlphaTest, retaining unknown knowledge when optional diagnostics detect native failure.</summary>
    internal void SetAlphaTest(bool value)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.AlphaTest);
        if (GpuSupport.Graphics.CoreProfile) { if (value) throw new NotSupportedException("AlphaTest requires compatibility profile."); return; }
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.AlphaTest) && rasterizer.AlphaTest == value) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.AlphaTest;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        SetEnable(EnableCap.AlphaTest, value);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.AlphaTest = value;
        rasterizerKnown |= RasterizerStateKnowledge.AlphaTest;
    }
    /// <summary>Establishes PointSmooth, retaining unknown knowledge when optional diagnostics detect native failure.</summary>
    internal void SetPointSmooth(bool value)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PointSmooth);
        if (GpuSupport.Graphics.CoreProfile) { if (value) throw new NotSupportedException("PointSmooth requires compatibility profile."); return; }
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PointSmooth) && rasterizer.PointSmooth == value) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PointSmooth;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        SetEnable(EnableCap.PointSmooth, value);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PointSmooth = value;
        rasterizerKnown |= RasterizerStateKnowledge.PointSmooth;
    }
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
    /// <summary>Establishes LineStipple, retaining unknown knowledge when optional diagnostics detect native failure.</summary>
    internal void SetLineStipple(bool value)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.LineStipple);
        if (GpuSupport.Graphics.CoreProfile) { if (value) throw new NotSupportedException("LineStipple requires compatibility profile."); return; }
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineStipple) && rasterizer.LineStipple == value) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.LineStipple;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        SetEnable(EnableCap.LineStipple, value);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.LineStipple = value;
        rasterizerKnown |= RasterizerStateKnowledge.LineStipple;
    }
    /// <summary>Establishes PolygonStipple, retaining unknown knowledge when optional diagnostics detect native failure.</summary>
    internal void SetPolygonStipple(bool value)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonStipple);
        if (GpuSupport.Graphics.CoreProfile) { if (value) throw new NotSupportedException("PolygonStipple requires compatibility profile."); return; }
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonStipple) && rasterizer.PolygonStipple == value) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonStipple;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        SetEnable(EnableCap.PolygonStipple, value);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonStipple = value;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonStipple;
    }
    #endregion
}
