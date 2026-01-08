using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Helper class for mapping OpenGL texture formats.
/// Provides consistent format/type pairs for texture creation.
/// </summary>
public static class TextureFormatHelper
{
    /// <summary>
    /// Gets the appropriate PixelFormat for a given internal format.
    /// </summary>
    public static PixelFormat GetPixelFormat(PixelInternalFormat internalFormat)
    {
        return internalFormat switch
        {
            PixelInternalFormat.Rgba16f => PixelFormat.Rgba,
            PixelInternalFormat.Rgba32f => PixelFormat.Rgba,
            PixelInternalFormat.Rgba8 => PixelFormat.Rgba,
            PixelInternalFormat.Rgba => PixelFormat.Rgba,
            PixelInternalFormat.Rgb16f => PixelFormat.Rgb,
            PixelInternalFormat.Rgb32f => PixelFormat.Rgb,
            PixelInternalFormat.Rgb8 => PixelFormat.Rgb,
            PixelInternalFormat.Rgb => PixelFormat.Rgb,
            PixelInternalFormat.Rg16f => PixelFormat.Rg,
            PixelInternalFormat.Rg32f => PixelFormat.Rg,
            PixelInternalFormat.R16f => PixelFormat.Red,
            PixelInternalFormat.R32f => PixelFormat.Red,
            PixelInternalFormat.DepthComponent => PixelFormat.DepthComponent,
            PixelInternalFormat.DepthComponent16 => PixelFormat.DepthComponent,
            PixelInternalFormat.DepthComponent24 => PixelFormat.DepthComponent,
            PixelInternalFormat.DepthComponent32f => PixelFormat.DepthComponent,
            PixelInternalFormat.Depth24Stencil8 => PixelFormat.DepthStencil,
            PixelInternalFormat.Depth32fStencil8 => PixelFormat.DepthStencil,
            _ => PixelFormat.Rgba
        };
    }

    /// <summary>
    /// Gets the appropriate PixelType for a given internal format.
    /// </summary>
    public static PixelType GetPixelType(PixelInternalFormat internalFormat)
    {
        return internalFormat switch
        {
            PixelInternalFormat.Rgba16f => PixelType.HalfFloat,
            PixelInternalFormat.Rgba32f => PixelType.Float,
            PixelInternalFormat.Rgba8 => PixelType.UnsignedByte,
            PixelInternalFormat.Rgba => PixelType.UnsignedByte,
            PixelInternalFormat.Rgb16f => PixelType.HalfFloat,
            PixelInternalFormat.Rgb32f => PixelType.Float,
            PixelInternalFormat.Rgb8 => PixelType.UnsignedByte,
            PixelInternalFormat.Rgb => PixelType.UnsignedByte,
            PixelInternalFormat.Rg16f => PixelType.HalfFloat,
            PixelInternalFormat.Rg32f => PixelType.Float,
            PixelInternalFormat.R16f => PixelType.HalfFloat,
            PixelInternalFormat.R32f => PixelType.Float,
            PixelInternalFormat.DepthComponent => PixelType.Float,
            PixelInternalFormat.DepthComponent16 => PixelType.UnsignedShort,
            PixelInternalFormat.DepthComponent24 => PixelType.UnsignedInt,
            PixelInternalFormat.DepthComponent32f => PixelType.Float,
            PixelInternalFormat.Depth24Stencil8 => PixelType.UnsignedInt248,
            PixelInternalFormat.Depth32fStencil8 => PixelType.Float32UnsignedInt248Rev,
            _ => PixelType.UnsignedByte
        };
    }

    /// <summary>
    /// Checks if the format is a depth or depth-stencil format.
    /// </summary>
    public static bool IsDepthFormat(PixelInternalFormat internalFormat)
    {
        return internalFormat switch
        {
            PixelInternalFormat.DepthComponent => true,
            PixelInternalFormat.DepthComponent16 => true,
            PixelInternalFormat.DepthComponent24 => true,
            PixelInternalFormat.DepthComponent32f => true,
            PixelInternalFormat.Depth24Stencil8 => true,
            PixelInternalFormat.Depth32fStencil8 => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the OpenGL filter parameter value for a filter mode.
    /// </summary>
    public static int GetFilterParameter(TextureFilterMode mode)
    {
        return mode switch
        {
            TextureFilterMode.Nearest => (int)TextureMinFilter.Nearest,
            TextureFilterMode.Linear => (int)TextureMinFilter.Linear,
            _ => (int)TextureMinFilter.Nearest
        };
    }
}
