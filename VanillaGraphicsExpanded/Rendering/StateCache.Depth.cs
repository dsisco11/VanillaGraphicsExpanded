using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns Depth transitions and independent field validity.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes Comparison, suppressing a known identical transition.</summary>
    public void SetDepthFunc(DepthFunction function)
    {
        if (!System.Enum.IsDefined(function)) throw new System.ArgumentOutOfRangeException(nameof(function));
        SynchronizeContext();
        if (depthKnown.HasFlag(DepthStateKnowledge.Comparison) && depth.Comparison == function) return;
        depthKnown &= ~DepthStateKnowledge.Comparison;
        GL.DepthFunc(function);
        FixedFunctionCalls++;
        depth.Comparison = function;
        depthKnown |= DepthStateKnowledge.Comparison;
    }
    /// <summary>Establishes WriteEnabled, suppressing a known identical transition.</summary>
    public void SetDepthWriteMask(bool enabled)
    {
        SynchronizeContext();
        if (depthKnown.HasFlag(DepthStateKnowledge.WriteEnabled) && depth.WriteEnabled == enabled) return;
        depthKnown &= ~DepthStateKnowledge.WriteEnabled;
        GL.DepthMask(enabled);
        FixedFunctionCalls++;
        depth.WriteEnabled = enabled;
        depthKnown |= DepthStateKnowledge.WriteEnabled;
    }
    #endregion
}
