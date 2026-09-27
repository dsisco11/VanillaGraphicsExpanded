namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Selects mutually exclusive terrain height treatments; the requested mode survives resource failures.</summary>
public enum TerrainSurfaceDetailMode
{
    Disabled,
    Relief,
    Tessellation
}
