using System.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

/// <summary>Snapshot data for one cell: 0 unavailable, 1 empty, 2 opaque with a material index.</summary>
internal readonly record struct LocalTraceSourceCell(uint Geometry, Vector4 Light);
