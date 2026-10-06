using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete output values independent of cache knowledge and native operations.</summary>
internal struct OutputState
{
    // Only boolean value bits are stored here; cache validity remains a separate category mask.
    private OutputStateKnowledge booleanValues;

    /// <summary>Declared or observed LogicOp LogicOperation value.</summary>
    public LogicOp LogicOperation;
    #region Public API
    /// <summary>Cached native FramebufferSrgb enable value.</summary>
    public bool FramebufferSrgb
    {
        readonly get => booleanValues.HasFlag(OutputStateKnowledge.FramebufferSrgb);
        set
        {
            if (value) booleanValues |= OutputStateKnowledge.FramebufferSrgb;
            else booleanValues &= ~OutputStateKnowledge.FramebufferSrgb;
        }
    }

    /// <summary>Cached native Dither enable value.</summary>
    public bool Dither
    {
        readonly get => booleanValues.HasFlag(OutputStateKnowledge.Dither);
        set
        {
            if (value) booleanValues |= OutputStateKnowledge.Dither;
            else booleanValues &= ~OutputStateKnowledge.Dither;
        }
    }

    /// <summary>Cached native ColorLogicOp enable value.</summary>
    public bool ColorLogicOp
    {
        readonly get => booleanValues.HasFlag(OutputStateKnowledge.ColorLogicOp);
        set
        {
            if (value) booleanValues |= OutputStateKnowledge.ColorLogicOp;
            else booleanValues &= ~OutputStateKnowledge.ColorLogicOp;
        }
    }
    #endregion
}
