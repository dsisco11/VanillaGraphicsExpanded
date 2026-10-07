namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Bounds pass operations in framebuffer pixels, independently of draw scissor state.</summary>
internal readonly record struct RenderArea(int X, int Y, int Width, int Height);
