using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Explicit 32-byte native range description; render pass and atlas remain native-table owned.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct ShadowGeometryRange(int PoolId, int IndexStart, int IndexCount,
    int VertexStart, int VertexCount, int Detail, uint RevisionLow, uint RevisionHigh);
