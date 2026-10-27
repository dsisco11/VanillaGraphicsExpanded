using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns Depth transitions and independent field validity.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes Comparison, suppressing a known identical transition.</summary>
    internal void SetDepthFunc(DepthFunction function)
    {
        ValidateBoundaryMutation(depth: DepthStateKnowledge.Comparison);
        if (!System.Enum.IsDefined(function)) throw new System.ArgumentOutOfRangeException(nameof(function));
        if (depthKnown.HasFlag(DepthStateKnowledge.Comparison) && depth.Comparison == function) return;
        depthKnown &= ~DepthStateKnowledge.Comparison;
        OpenGLStateEffects.DepthFunc(function);
#if DEBUG
        FixedFunctionCalls++;
#endif
        depth.Comparison = function;
        depthKnown |= DepthStateKnowledge.Comparison;
    }
    /// <summary>Establishes WriteEnabled, suppressing a known identical transition.</summary>
    internal void SetDepthWriteMask(bool enabled)
    {
        ValidateBoundaryMutation(depth: DepthStateKnowledge.WriteEnabled);
        if (depthKnown.HasFlag(DepthStateKnowledge.WriteEnabled) && depth.WriteEnabled == enabled) return;
        depthKnown &= ~DepthStateKnowledge.WriteEnabled;
        OpenGLStateEffects.DepthMask(enabled);
#if DEBUG
        FixedFunctionCalls++;
#endif
        depth.WriteEnabled = enabled;
        depthKnown |= DepthStateKnowledge.WriteEnabled;
    }
    #endregion
}
