namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Bounds transverse slopes against one fixed-sign, nonzero lightward axis.</summary>
internal readonly record struct ShadowCandidateSector(int Axis, int Sign, double MinA, double MaxA, double MinB, double MaxB);
