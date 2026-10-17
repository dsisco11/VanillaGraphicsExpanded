using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains explicit output interpretation and logic-operation neutralization.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Applies output groups conservatively rather than inferring shader color usage.</summary>
    private void ApplyPipelineOutput(OutputDesc value, OutputStateKnowledge relevant)
    {
        if (relevant.HasFlag(OutputStateKnowledge.FramebufferSrgb)) SetFramebufferSrgbEnabled(value.FramebufferSrgb);
        if (relevant.HasFlag(OutputStateKnowledge.Dither)) SetDitherEnabled(value.Dither);
        if (relevant.HasFlag(OutputStateKnowledge.ColorLogicOp)) SetColorLogicOpEnabled(false);
        if (relevant.HasFlag(OutputStateKnowledge.LogicOperation)) SetLogicOperation(LogicOp.Copy);
    }
    #endregion
}
