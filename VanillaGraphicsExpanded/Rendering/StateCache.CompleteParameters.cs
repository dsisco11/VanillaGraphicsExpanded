using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns previously untracked native graphics parameter transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region Depth and raster parameters
    /// <summary>Establishes DepthRange while suppressing a known identical native transition.</summary>
    internal void SetDepthRange(double near, double far)
    {
        if (!double.IsFinite(near) || !double.IsFinite(far) || near < 0 || near > 1 || far < 0 || far > 1) throw new ArgumentOutOfRangeException(nameof(near));
        ValidateCompleteMutation();
        if (depth.SupplementalKnown.HasFlag(CompleteDepthKnowledge.DepthRange) && depth.DepthRange == (near, far)) return;
        depth.SupplementalKnown &= ~CompleteDepthKnowledge.DepthRange;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.DepthRange(near, far);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        depth.DepthRange = (near, far);
        depth.SupplementalKnown |= CompleteDepthKnowledge.DepthRange;
    }
    /// <summary>Establishes CullMode while suppressing a known identical native transition.</summary>
    internal void SetCullMode(CullFaceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateCompleteMutation();
        if (rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.CullMode) && rasterizer.CullMode == mode) return;
        rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.CullMode;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.CullFace(mode);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.CullMode = mode;
        rasterizer.SupplementalKnown |= CompleteRasterKnowledge.CullMode;
    }
    /// <summary>Establishes FrontFace while suppressing a known identical native transition.</summary>
    internal void SetFrontFace(FrontFaceDirection winding)
    {
        if (!Enum.IsDefined(winding)) throw new ArgumentOutOfRangeException(nameof(winding));
        ValidateCompleteMutation();
        if (rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.FrontFace) && rasterizer.FrontFace == winding) return;
        rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.FrontFace;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.FrontFace(winding);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.FrontFace = winding;
        rasterizer.SupplementalKnown |= CompleteRasterKnowledge.FrontFace;
    }
    /// <summary>Establishes PolygonOffset while suppressing a known identical native transition.</summary>
    internal void SetPolygonOffset(float factor, float units)
    {
        if (!float.IsFinite(factor) || !float.IsFinite(units)) throw new ArgumentOutOfRangeException(nameof(factor));
        ValidateCompleteMutation();
        if (rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonOffset) && rasterizer.PolygonOffset == (factor, units)) return;
        rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.PolygonOffset;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.PolygonOffset(factor, units);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffset = (factor, units);
        rasterizer.SupplementalKnown |= CompleteRasterKnowledge.PolygonOffset;
    }
    #endregion
    #region Sampling and assembly parameters
    /// <summary>Establishes SampleCoverage while suppressing a known identical native transition.</summary>
    internal void SetSampleCoverage(float value, bool invert)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        ValidateCompleteMutation();
        if (completeSampling.Known.HasFlag(CompleteSamplingKnowledge.SampleCoverage) && completeSampling.SampleCoverage == (value, invert)) return;
        completeSampling.Known &= ~CompleteSamplingKnowledge.SampleCoverage;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.SampleCoverage(value, invert);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        completeSampling.SampleCoverage = (value, invert);
        completeSampling.Known |= CompleteSamplingKnowledge.SampleCoverage;
    }
    /// <summary>Establishes MinimumSampleShading while suppressing a known identical native transition.</summary>
    internal void SetMinimumSampleShading(float value)
    {
        if (!GpuSupport.Graphics.SampleShading) throw new NotSupportedException("Sample shading is unavailable.");
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        ValidateCompleteMutation();
        if (completeSampling.Known.HasFlag(CompleteSamplingKnowledge.MinimumSampleShading) && completeSampling.MinimumSampleShading == value) return;
        completeSampling.Known &= ~CompleteSamplingKnowledge.MinimumSampleShading;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.MinSampleShading(value);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        completeSampling.MinimumSampleShading = value;
        completeSampling.Known |= CompleteSamplingKnowledge.MinimumSampleShading;
    }
    /// <summary>Establishes RestartIndex while suppressing a known identical native transition.</summary>
    internal void SetRestartIndex(uint index)
    {
        
        ValidateCompleteMutation();
        if (assembly.SupplementalKnown.HasFlag(CompleteAssemblyKnowledge.RestartIndex) && assembly.RestartIndex == index) return;
        assembly.SupplementalKnown &= ~CompleteAssemblyKnowledge.RestartIndex;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.PrimitiveRestartIndex(index);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        assembly.RestartIndex = index;
        assembly.SupplementalKnown |= CompleteAssemblyKnowledge.RestartIndex;
    }
    #endregion
    #region Draw dynamics
    /// <summary>Establishes Scissor while suppressing a known identical native transition.</summary>
    internal void SetScissor(int x, int y, int width, int height)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        ValidateCompleteMutation();
        if (dynamicState.SupplementalKnown.HasFlag(CompleteDynamicKnowledge.Scissor) && dynamicState.Scissor == (x, y, width, height)) return;
        dynamicState.SupplementalKnown &= ~CompleteDynamicKnowledge.Scissor;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.Scissor(x, y, width, height);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        dynamicState.Scissor = (x, y, width, height);
        dynamicState.SupplementalKnown |= CompleteDynamicKnowledge.Scissor;
    }
    /// <summary>Establishes BlendConstant while suppressing a known identical native transition.</summary>
    internal void SetBlendConstant(float r, float g, float b, float a)
    {
        if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b) || !float.IsFinite(a)) throw new ArgumentOutOfRangeException(nameof(r));
        r = Math.Clamp(r, 0, 1); g = Math.Clamp(g, 0, 1); b = Math.Clamp(b, 0, 1); a = Math.Clamp(a, 0, 1);
        ValidateCompleteMutation();
        if (dynamicState.SupplementalKnown.HasFlag(CompleteDynamicKnowledge.BlendConstant) && dynamicState.BlendConstant == (r, g, b, a)) return;
        dynamicState.SupplementalKnown &= ~CompleteDynamicKnowledge.BlendConstant;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.BlendColor(r, g, b, a);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        dynamicState.BlendConstant = (r, g, b, a);
        dynamicState.SupplementalKnown |= CompleteDynamicKnowledge.BlendConstant;
    }
    #endregion
    #endregion
}
