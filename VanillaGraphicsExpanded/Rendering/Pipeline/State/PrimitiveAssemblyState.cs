using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete PrimitiveAssemblyState values independent of cache knowledge and native operations.</summary>
internal struct PrimitiveAssemblyState
{
    /// <summary>Declared PatchVertices value.</summary>
    public int PatchVertices;
    /// <summary>Declared or observed uint RestartIndex value.</summary>
    public uint RestartIndex;
    /// <summary>Cached native PrimitiveRestart enable value.</summary>
    public bool PrimitiveRestart;
    /// <summary>Cached native PrimitiveRestartFixedIndex enable value.</summary>
    public bool PrimitiveRestartFixedIndex;
}
