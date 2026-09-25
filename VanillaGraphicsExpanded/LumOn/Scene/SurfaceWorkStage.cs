namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Independent Surface Cache dispatch families, including publication overhead.</summary>
internal enum SurfaceWorkStage { Capture, Seed, Indirect, Direct, Combine, Reset, Count }
