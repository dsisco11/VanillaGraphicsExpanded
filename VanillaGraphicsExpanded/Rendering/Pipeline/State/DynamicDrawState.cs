using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete DynamicDrawState values independent of cache knowledge and native operations.</summary>
internal struct DynamicDrawState
{
    /// <summary>Declared X value.</summary>
    public int X;
    /// <summary>Declared Y value.</summary>
    public int Y;
    /// <summary>Declared Width value.</summary>
    public int Width;
    /// <summary>Declared Height value.</summary>
    public int Height;
    /// <summary>Independent knowledge for observed native values.</summary>
    public CompleteDynamicKnowledge SupplementalKnown;
    /// <summary>Supplemental native enable values and their validity.</summary>
    public CompleteEnableState SupplementalEnables;
    /// <summary>Observed Scissor value, valid only when corresponding knowledge is established.</summary>
    public (int X, int Y, int Width, int Height) Scissor;
    /// <summary>Observed BlendConstant value, valid only when corresponding knowledge is established.</summary>
    public (float R, float G, float B, float A) BlendConstant;
}
