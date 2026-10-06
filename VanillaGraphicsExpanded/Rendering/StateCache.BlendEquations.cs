using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached blend equations transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes equations across every native output slot.</summary>
    internal void SetBlendEquation(BlendEquationMode rgb, BlendEquationMode alpha)
    {
        if (!Enum.IsDefined(rgb) || !Enum.IsDefined(alpha)) throw new ArgumentOutOfRangeException(nameof(rgb));
        ValidateBoundaryMutation(blend: BlendStateKnowledge.Equations);
        int count = MaxDrawBuffers;
        bool equal = true;
        for (int i = 0; i < count; i++) equal &= TryGetCachedBlendEquation(i, out var known) && known == (rgb, alpha);
        if (equal) return;
        ForgetBlendEquations();
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.BlendEquationSeparate(rgb, alpha); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        for (int i = 0; i < count; i++) { blend[i].Equations = (rgb, alpha); blendKnown[i] |= BlendStateKnowledge.Equations; }
    }

    /// <summary>Establishes equations for a single draw-output slot.</summary>
    internal void SetBlendEquationIndexed(int index, BlendEquationMode rgb, BlendEquationMode alpha)
    {
        ValidateDrawOutput(index);
        if (!Enum.IsDefined(rgb) || !Enum.IsDefined(alpha)) throw new ArgumentOutOfRangeException(nameof(rgb));
        ValidateBoundaryMutation(blend: BlendStateKnowledge.Equations, index: index);
        if (!GpuSupport.Graphics.IndependentBlend) throw new NotSupportedException("Independent blend equations are unavailable.");
        if (TryGetCachedBlendEquation(index, out var known) && known == (rgb, alpha)) return;
        ForgetBlendEquation(index);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.BlendEquationSeparate(index, rgb, alpha); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        blend[index].Equations = (rgb, alpha); blendKnown[index] |= BlendStateKnowledge.Equations;
    }

    /// <summary>Reads independently valid equation knowledge without querying native state.</summary>
    internal bool TryGetCachedBlendEquation(int index, out (BlendEquationMode Rgb, BlendEquationMode Alpha) equations)
    {
        equations = default;
        if (index < 0 || index >= blend.Length || !blendKnown[index].HasFlag(BlendStateKnowledge.Equations)) return false;
        equations = blend[index].Equations; return true;
    }

    /// <summary>Withdraws one equation pair after an uncertain native transition.</summary>
    internal void ForgetBlendEquation(int index)
    {
        if (index >= 0 && index < blendKnown.Length) blendKnown[index] &= ~BlendStateKnowledge.Equations;
    }

    /// <summary>Withdraws the equation alias group after an external global mutation.</summary>
    internal void ForgetBlendEquations()
    {
        for (int i = 0; i < blendKnown.Length; i++) blendKnown[i] &= ~BlendStateKnowledge.Equations;
    }
    #endregion
}
