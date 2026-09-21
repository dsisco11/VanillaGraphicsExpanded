using System.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Snapshot data for one cell: 0 unavailable, 1 empty, 2 opaque with a material index.</summary>
internal readonly record struct NearFieldSourceCell(uint Geometry, Vector4 Light);
