using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete BlendState values independent of cache knowledge and native operations.</summary>
internal struct BlendState
{
    /// <summary>Declared Enabled value.</summary>
    public bool Enabled;
    /// <summary>Declared Factors value.</summary>
    public GlBlendFunc Factors;
    /// <summary>Declared WriteMask value.</summary>
    public GlColorMask WriteMask;
    /// <summary>Effective separate RGB and alpha equations for this draw-output slot.</summary>
    public (BlendEquationMode Rgb, BlendEquationMode Alpha) Equations;
}
