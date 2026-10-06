using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete DepthState values independent of cache knowledge and native operations.</summary>
internal struct DepthState
{
    // Only boolean value bits are stored here; cache validity remains a separate category mask.
    private DepthStateKnowledge booleanValues;

    /// <summary>Declared Comparison value.</summary>
    public DepthFunction Comparison;
    /// <summary>Observed DepthRange value, valid only when corresponding knowledge is established.</summary>
    public (double Near, double Far) DepthRange;
    #region Public API
    /// <summary>Declared TestEnabled value.</summary>
    public bool TestEnabled
    {
        readonly get => booleanValues.HasFlag(DepthStateKnowledge.TestEnabled);
        set
        {
            if (value) booleanValues |= DepthStateKnowledge.TestEnabled;
            else booleanValues &= ~DepthStateKnowledge.TestEnabled;
        }
    }

    /// <summary>Declared WriteEnabled value.</summary>
    public bool WriteEnabled
    {
        readonly get => booleanValues.HasFlag(DepthStateKnowledge.WriteEnabled);
        set
        {
            if (value) booleanValues |= DepthStateKnowledge.WriteEnabled;
            else booleanValues &= ~DepthStateKnowledge.WriteEnabled;
        }
    }
    #endregion
}
