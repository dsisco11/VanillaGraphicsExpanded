using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes actual renderbuffer storage, including the driver-selected sample count.</summary>
public readonly record struct GpuRenderbufferMetadata(int Width, int Height, PixelInternalFormat Format, int Samples);
