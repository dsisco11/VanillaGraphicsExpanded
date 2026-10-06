using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete BlendState values independent of cache knowledge and native operations.</summary>
internal struct BlendState
{
    // Only boolean value bits are stored here; cache validity remains a separate category mask.
    private BlendStateKnowledge booleanValues;

    /// <summary>Declared Factors value.</summary>
    public GlBlendFunc Factors;
    /// <summary>Declared WriteMask value.</summary>
    public GlColorMask WriteMask;
    /// <summary>Effective separate RGB and alpha equations for this draw-output slot.</summary>
    public (BlendEquationMode Rgb, BlendEquationMode Alpha) Equations;
    #region Public API
    /// <summary>Declared Enabled value.</summary>
    public bool Enabled
    {
        readonly get => booleanValues.HasFlag(BlendStateKnowledge.Enabled);
        set
        {
            if (value) booleanValues |= BlendStateKnowledge.Enabled;
            else booleanValues &= ~BlendStateKnowledge.Enabled;
        }
    }
    #endregion
}
