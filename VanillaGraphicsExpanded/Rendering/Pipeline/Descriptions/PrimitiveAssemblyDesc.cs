using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Immutable draw topology, restart and tessellation policy.</summary>
public sealed record PrimitiveAssemblyDesc
{
    public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.Triangles;
    public bool Restart { get; init; }
    public bool FixedIndexRestart { get; init; }
    public uint RestartIndex { get; init; }
    public int PatchVertices { get; init; } = 3;
}
