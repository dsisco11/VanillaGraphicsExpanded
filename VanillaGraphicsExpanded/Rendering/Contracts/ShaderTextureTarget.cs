namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares sampler texture targets without depending on the graphics runtime.</summary>
/// <remarks>Values match OpenGL/OpenTK texture-target constants for direct runtime casts.</remarks>
internal enum ShaderTextureTarget
{
    Texture1D = 0x0DE0,
    Texture2D = 0x0DE1,
    Texture3D = 0x806F,
    TextureCubeMap = 0x8513,
    Texture2DArray = 0x8C1A,
    TextureBuffer = 0x8C2A
}
