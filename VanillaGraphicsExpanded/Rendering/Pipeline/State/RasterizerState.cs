using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete RasterizerState values independent of cache knowledge and native operations.</summary>
internal struct RasterizerState
{
    /// <summary>Declared CullEnabled value.</summary>
    public bool CullEnabled;
    /// <summary>Declared ScissorEnabled value.</summary>
    public bool ScissorEnabled;
    /// <summary>Declared LineWidth value.</summary>
    public float LineWidth;
    /// <summary>Declared PointSize value.</summary>
    public float PointSize;
    /// <summary>Declared ProvokingVertex value.</summary>
    public ProvokingVertexMode ProvokingVertex;
}
