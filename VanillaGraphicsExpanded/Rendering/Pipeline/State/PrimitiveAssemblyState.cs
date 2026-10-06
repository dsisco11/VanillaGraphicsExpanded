using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete PrimitiveAssemblyState values independent of cache knowledge and native operations.</summary>
internal struct PrimitiveAssemblyState
{
    /// <summary>Declared PatchVertices value.</summary>
    public int PatchVertices;
    /// <summary>Independent knowledge for observed native values.</summary>
    public CompleteAssemblyKnowledge SupplementalKnown;
    /// <summary>Supplemental native enable values and their validity.</summary>
    public CompleteEnableState SupplementalEnables;
    /// <summary>Declared or observed uint RestartIndex value.</summary>
    public uint RestartIndex;
}
