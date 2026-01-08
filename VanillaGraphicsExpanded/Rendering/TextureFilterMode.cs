namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Texture filtering mode for minification and magnification.
/// </summary>
public enum TextureFilterMode
{
    /// <summary>
    /// Nearest-neighbor filtering. Best for probe data, G-buffer data, depth textures.
    /// No interpolation - returns exact texel value.
    /// </summary>
    Nearest,

    /// <summary>
    /// Bilinear filtering. Best for scene textures, color buffers.
    /// Interpolates between neighboring texels for smoother results.
    /// </summary>
    Linear
}
