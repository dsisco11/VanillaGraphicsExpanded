using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached draw dynamics transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes Scissor while suppressing a known identical native transition.</summary>
    internal void SetScissor(int x, int y, int width, int height)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        ValidateCompleteMutation();
        if (dynamicKnown.HasFlag(DynamicDrawStateKnowledge.Scissor) && dynamicState.Scissor == (x, y, width, height)) return;
        dynamicKnown &= ~DynamicDrawStateKnowledge.Scissor;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.Scissor(x, y, width, height);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        dynamicState.Scissor = (x, y, width, height);
        dynamicKnown |= DynamicDrawStateKnowledge.Scissor;
    }

    /// <summary>Establishes BlendConstant while suppressing a known identical native transition.</summary>
    internal void SetBlendConstant(float r, float g, float b, float a)
    {
        if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b) || !float.IsFinite(a)) throw new ArgumentOutOfRangeException(nameof(r));
        r = Math.Clamp(r, 0, 1); g = Math.Clamp(g, 0, 1); b = Math.Clamp(b, 0, 1); a = Math.Clamp(a, 0, 1);
        ValidateCompleteMutation();
        if (dynamicKnown.HasFlag(DynamicDrawStateKnowledge.BlendConstant) && dynamicState.BlendConstant == (r, g, b, a)) return;
        dynamicKnown &= ~DynamicDrawStateKnowledge.BlendConstant;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.BlendColor(r, g, b, a);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        dynamicState.BlendConstant = (r, g, b, a);
        dynamicKnown |= DynamicDrawStateKnowledge.BlendConstant;
    }
    #endregion
}
