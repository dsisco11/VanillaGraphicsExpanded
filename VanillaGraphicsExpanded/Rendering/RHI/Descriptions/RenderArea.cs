namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Bounds pass operations in framebuffer pixels, independently of draw scissor state.</summary>
public readonly record struct RenderArea(int X, int Y, int Width, int Height);
