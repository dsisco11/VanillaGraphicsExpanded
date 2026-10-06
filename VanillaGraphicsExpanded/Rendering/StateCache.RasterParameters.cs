using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns cached transitions for configurable clipping and compatibility raster state.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Sets native clip conventions; fixed defaults require no extension call on older contexts.</summary>
    internal void SetClipControl(ClipOrigin origin, ClipDepthMode depth)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ClipControl);
        if (!Enum.IsDefined(origin) || !Enum.IsDefined(depth)) throw new ArgumentOutOfRangeException(nameof(origin));
        if (!GpuSupport.Graphics.ClipControl)
        {
            if (origin != ClipOrigin.LowerLeft || depth != ClipDepthMode.NegativeOneToOne) throw new NotSupportedException("Clip control unavailable.");
            return;
        }
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.ClipControl) && rasterizer.ClipOrigin == origin && rasterizer.ClipDepth == depth) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.ClipControl;
        CheckBoundaryNativeError();
        GL.ClipControl(origin, depth); FixedFunctionCalls++;
        CheckBoundaryNativeError();
        rasterizer.ClipOrigin = origin; rasterizer.ClipDepth = depth;
        rasterizerKnown |= RasterizerStateKnowledge.ClipControl;
    }
    /// <summary>Establishes point-coordinate orientation for programmable point rasterization.</summary>
    internal void SetPointSpriteOrigin(PointSpriteCoordOriginParameter value)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PointSpriteOrigin);
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PointSpriteOrigin) && rasterizer.PointSpriteOrigin == value) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PointSpriteOrigin;
        CheckBoundaryNativeError();
        GL.PointParameter(PointParameterName.PointSpriteCoordOrigin, (int)value); FixedFunctionCalls++;
        CheckBoundaryNativeError();
        rasterizer.PointSpriteOrigin = value; rasterizerKnown |= RasterizerStateKnowledge.PointSpriteOrigin;
    }
    /// <summary>Sets a comparison and native-clamped reference, including parameters while the alpha test is disabled.</summary>
    internal void SetAlphaFunction(AlphaFunction comparison, float reference)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.AlphaFunction);
        if (!Enum.IsDefined(comparison) || !float.IsFinite(reference)) throw new ArgumentOutOfRangeException(nameof(comparison));
        if (GpuSupport.Graphics.CoreProfile) throw new NotSupportedException("Alpha function requires compatibility profile.");
        reference = Math.Clamp(reference, 0, 1);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.AlphaFunction) && rasterizer.AlphaComparison == comparison && rasterizer.AlphaReference == reference) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.AlphaFunction;
        CheckBoundaryNativeError();
        GL.AlphaFunc(comparison, reference); FixedFunctionCalls++;
        CheckBoundaryNativeError();
        rasterizer.AlphaComparison = comparison; rasterizer.AlphaReference = reference;
        rasterizerKnown |= RasterizerStateKnowledge.AlphaFunction;
    }
    /// <summary>Sets the native-clamped repeat and complete unsigned 16-bit line pattern.</summary>
    internal void SetLineStipple(int factor, ushort pattern)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.LineStippleParameters);
        if (GpuSupport.Graphics.CoreProfile) throw new NotSupportedException("Line stipple requires compatibility profile.");
        factor = Math.Clamp(factor, 1, 256);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineStippleParameters) && rasterizer.LineStippleFactor == factor && rasterizer.LineStipplePattern == pattern) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.LineStippleParameters;
        CheckBoundaryNativeError();
        GL.LineStipple(factor, pattern); FixedFunctionCalls++;
        CheckBoundaryNativeError();
        rasterizer.LineStippleFactor = factor; rasterizer.LineStipplePattern = pattern;
        rasterizerKnown |= RasterizerStateKnowledge.LineStippleParameters;
    }
    /// <summary>Changes one clip enable without reading or overwriting any other distance.</summary>
    internal void SetClipDistance(int index, bool enabled)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ClipDistances);
        int count = GpuSupport.Graphics.MaxClipDistances;
        if (count > 32) throw new NotSupportedException("Clip-distance mask exceeds 32 bits.");
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        uint bit = 1u << index;
        if ((clipDistancesKnown & bit) != 0 && ((rasterizer.ClipDistances & bit) != 0) == enabled) return;
        clipDistancesKnown &= ~bit; rasterizerKnown &= ~RasterizerStateKnowledge.ClipDistances;
        CheckBoundaryNativeError();
        SetEnable((EnableCap)((int)EnableCap.ClipDistance0 + index), enabled);
        CheckBoundaryNativeError();
        if (enabled) rasterizer.ClipDistances |= bit; else rasterizer.ClipDistances &= ~bit;
        clipDistancesKnown |= bit;
        uint complete = count == 32 ? uint.MaxValue : (1u << count) - 1;
        if ((clipDistancesKnown & complete) == complete) rasterizerKnown |= RasterizerStateKnowledge.ClipDistances;
    }
    #endregion
}
