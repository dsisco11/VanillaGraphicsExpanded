using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>One current native range's bounded command address, eligibility and conservative geometry cost.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct ShadowDrawRange(uint Group, uint Command, uint Eligible, uint TriangleCost,
    uint IndexStart, uint IndexCount, int BaseVertex, uint RevisionLow, uint RevisionHigh,
    uint VertexStart, uint VertexCount);
