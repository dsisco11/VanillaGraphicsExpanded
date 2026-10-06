using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete PrimitiveAssemblyState values independent of cache knowledge and native operations.</summary>
internal struct PrimitiveAssemblyState
{
    // Only boolean value bits are stored here; cache validity remains a separate category mask.
    private PrimitiveAssemblyStateKnowledge booleanValues;

    /// <summary>Declared PatchVertices value.</summary>
    public int PatchVertices;
    /// <summary>Declared or observed uint RestartIndex value.</summary>
    public uint RestartIndex;
    #region Public API
    /// <summary>Cached native PrimitiveRestart enable value.</summary>
    public bool PrimitiveRestart
    {
        readonly get => booleanValues.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestart);
        set
        {
            if (value) booleanValues |= PrimitiveAssemblyStateKnowledge.PrimitiveRestart;
            else booleanValues &= ~PrimitiveAssemblyStateKnowledge.PrimitiveRestart;
        }
    }

    /// <summary>Cached native PrimitiveRestartFixedIndex enable value.</summary>
    public bool PrimitiveRestartFixedIndex
    {
        readonly get => booleanValues.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex);
        set
        {
            if (value) booleanValues |= PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
            else booleanValues &= ~PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
        }
    }
    #endregion
}
