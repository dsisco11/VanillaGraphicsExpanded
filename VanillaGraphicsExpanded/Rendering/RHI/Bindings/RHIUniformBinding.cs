namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Pairs an authored uniform-block entry with a captured logical version, independently of physical storage.</summary>
public readonly record struct RHIUniformBinding(ulong Identity, RHIUniformSnapshot? Snapshot);
