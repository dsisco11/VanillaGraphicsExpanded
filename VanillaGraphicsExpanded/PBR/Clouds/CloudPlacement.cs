using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Copies committed placement without retaining worker state or mutable engine vectors. Distances are world units.</summary>
internal readonly record struct CloudPlacement(int CenterX, int CenterZ, int TileX, int TileZ,
    int WindTileX, int WindTileZ, double WindX, double WindZ, Vector3 Offset, float Clock)
{
    /// <summary>Rejects invalid placement before a texture can be submitted to transport.</summary>
    internal bool IsFinite => double.IsFinite(WindX) && double.IsFinite(WindZ) &&
        float.IsFinite(Offset.X) && float.IsFinite(Offset.Y) && float.IsFinite(Offset.Z) && float.IsFinite(Clock);
}

