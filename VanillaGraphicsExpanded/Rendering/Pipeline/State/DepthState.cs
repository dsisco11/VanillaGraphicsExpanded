using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete DepthState values independent of cache knowledge and native operations.</summary>
internal struct DepthState
{
    /// <summary>Declared TestEnabled value.</summary>
    public bool TestEnabled;
    /// <summary>Declared Comparison value.</summary>
    public DepthFunction Comparison;
    /// <summary>Declared WriteEnabled value.</summary>
    public bool WriteEnabled;
    /// <summary>Independent knowledge for observed native values.</summary>
    public CompleteDepthKnowledge SupplementalKnown;
    /// <summary>Supplemental native enable values and their validity.</summary>
    public CompleteEnableState SupplementalEnables;
    /// <summary>Observed DepthRange value, valid only when corresponding knowledge is established.</summary>
    public (double Near, double Far) DepthRange;
}
