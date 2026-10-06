using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached depth range transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes DepthRange while suppressing a known identical native transition.</summary>
    internal void SetDepthRange(double near, double far)
    {
        if (!double.IsFinite(near) || !double.IsFinite(far) || near < 0 || near > 1 || far < 0 || far > 1) throw new ArgumentOutOfRangeException(nameof(near));
        ValidateCompleteMutation();
        if (depthKnown.HasFlag(DepthStateKnowledge.DepthRange) && depth.DepthRange == (near, far)) return;
        depthKnown &= ~DepthStateKnowledge.DepthRange;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.DepthRange(near, far);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        depth.DepthRange = (near, far);
        depthKnown |= DepthStateKnowledge.DepthRange;
    }
    #endregion
}
