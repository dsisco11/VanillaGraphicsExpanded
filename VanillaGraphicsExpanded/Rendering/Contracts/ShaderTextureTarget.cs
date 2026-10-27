namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares sampler texture targets without depending on the graphics runtime.</summary>
/// <remarks>Values identify logical shader-contract dimensions, independently of native API tokens.</remarks>
internal enum ShaderTextureTarget
{
    Texture1D = 1,
    Texture2D = 2,
    Texture3D = 3,
    TextureCubeMap = 6,
    Texture2DArray = 5,
    TextureBuffer = 11
}
