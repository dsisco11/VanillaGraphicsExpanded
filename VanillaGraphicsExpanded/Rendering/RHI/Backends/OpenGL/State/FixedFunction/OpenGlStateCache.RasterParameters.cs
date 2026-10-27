using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns cached transitions for configurable clipping and raster state.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes core clip conventions on the admitted OpenGL context.</summary>
    internal void SetClipControl(ClipOrigin origin, ClipDepthMode depth)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ClipControl);
        if (!Enum.IsDefined(origin) || !Enum.IsDefined(depth)) throw new ArgumentOutOfRangeException(nameof(origin));

        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.ClipControl) && rasterizer.ClipOrigin == origin && rasterizer.ClipDepth == depth) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.ClipControl;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.ClipControl(origin, depth);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
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
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.PointParameter(PointParameterName.PointSpriteCoordOrigin, (int)value);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PointSpriteOrigin = value; rasterizerKnown |= RasterizerStateKnowledge.PointSpriteOrigin;
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
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        SetEnable((EnableCap)((int)EnableCap.ClipDistance0 + index), enabled);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) rasterizer.ClipDistances |= bit; else rasterizer.ClipDistances &= ~bit;
        clipDistancesKnown |= bit;
        uint complete = count == 32 ? uint.MaxValue : (1u << count) - 1;
        if ((clipDistancesKnown & complete) == complete) rasterizerKnown |= RasterizerStateKnowledge.ClipDistances;
    }
    #endregion
}
