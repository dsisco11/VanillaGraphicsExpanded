namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Freezes the existing native tessellation family's shared camera metric and current bounded policy.</summary>
internal readonly record struct TerrainSubdivisionInputs(long Frame, bool Enabled, float EdgePixels,
    float MaximumLevel, float FadeStart, float FadeEnd, float FocalPixels);
