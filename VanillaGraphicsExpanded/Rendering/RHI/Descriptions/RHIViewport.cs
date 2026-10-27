namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Specifies a viewport rectangle independently of executed cache knowledge or native selectors.</summary>
public readonly record struct RHIViewport(int X, int Y, int Width, int Height);
