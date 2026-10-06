using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Immutable draw topology, restart and tessellation policy.</summary>
internal sealed record PrimitiveAssemblyDesc
{
    public PrimitiveType Topology { get; init; } = PrimitiveType.Triangles;
    public bool Restart { get; init; }
    public bool FixedIndexRestart { get; init; }
    public uint RestartIndex { get; init; }
    public int PatchVertices { get; init; } = 3;
}
