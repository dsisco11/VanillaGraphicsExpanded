using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>One output slot's format, or an unrouted hole with an explicit active-output discard policy.</summary>
internal readonly record struct ColorTargetSlot(PixelInternalFormat? Format, bool DiscardOutput = false);
