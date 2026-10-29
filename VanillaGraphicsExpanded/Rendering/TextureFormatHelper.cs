using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Classifies semantic texture storage and client pixel representations.
/// Provides consistent format/type pairs for texture creation.
/// </summary>
public static class TextureFormatHelper
{
    /// <summary>
    /// Gets the appropriate PixelChannelLayout for a given internal format.
    /// </summary>
    public static PixelChannelLayout GetPixelFormat(TextureFormat internalFormat)
    {
        return internalFormat switch
        {
            TextureFormat.CompressedR11Eac or TextureFormat.CompressedSignedR11Eac => PixelChannelLayout.Red,
            TextureFormat.CompressedRg11Eac or TextureFormat.CompressedSignedRg11Eac => PixelChannelLayout.Rg,
            TextureFormat.CompressedRgb8Etc2 or TextureFormat.CompressedSrgb8Etc2
                or TextureFormat.CompressedRgb8PunchthroughAlpha1Etc2 or TextureFormat.CompressedSrgb8PunchthroughAlpha1Etc2 => PixelChannelLayout.Rgb,
            TextureFormat.CompressedRgba8Etc2Eac or TextureFormat.CompressedSrgb8Alpha8Etc2Eac => PixelChannelLayout.Rgba,
            TextureFormat.R8i or TextureFormat.R16i or TextureFormat.R32i or TextureFormat.R8ui or TextureFormat.R16ui or TextureFormat.R32ui => PixelChannelLayout.RedInteger,
            TextureFormat.Rg8i or TextureFormat.Rg16i or TextureFormat.Rg32i or TextureFormat.Rg8ui or TextureFormat.Rg16ui or TextureFormat.Rg32ui => PixelChannelLayout.RgInteger,
            TextureFormat.Rgb8i or TextureFormat.Rgb16i or TextureFormat.Rgb32i or TextureFormat.Rgb8ui or TextureFormat.Rgb16ui or TextureFormat.Rgb32ui => PixelChannelLayout.RgbInteger,
            TextureFormat.Rgba8i or TextureFormat.Rgba16i or TextureFormat.Rgba32i or TextureFormat.Rgba8ui or TextureFormat.Rgba16ui or TextureFormat.Rgba32ui or TextureFormat.Rgb10A2ui => PixelChannelLayout.RgbaInteger,
            TextureFormat.Rgba16f => PixelChannelLayout.Rgba,
            TextureFormat.Rgba32f => PixelChannelLayout.Rgba,
            TextureFormat.Rgba16 => PixelChannelLayout.Rgba,
            TextureFormat.Rgba8 => PixelChannelLayout.Rgba,
            TextureFormat.Rgba => PixelChannelLayout.Rgba,
            TextureFormat.R11fG11fB10f => PixelChannelLayout.Rgb,
            TextureFormat.Rgb16f => PixelChannelLayout.Rgb,
            TextureFormat.Rgb32f => PixelChannelLayout.Rgb,
            TextureFormat.Rgb16 => PixelChannelLayout.Rgb,
            TextureFormat.Rgb8 => PixelChannelLayout.Rgb,
            TextureFormat.Rgb => PixelChannelLayout.Rgb,
            TextureFormat.Rg16f => PixelChannelLayout.Rg,
            TextureFormat.Rg32f => PixelChannelLayout.Rg,
            TextureFormat.Rg16 => PixelChannelLayout.Rg,
            TextureFormat.Rg8 => PixelChannelLayout.Rg,
            TextureFormat.R16f => PixelChannelLayout.Red,
            TextureFormat.R32f => PixelChannelLayout.Red,
            TextureFormat.R16 => PixelChannelLayout.Red,
            TextureFormat.R8 => PixelChannelLayout.Red,
            TextureFormat.DepthComponent => PixelChannelLayout.Depth,
            TextureFormat.DepthComponent16 => PixelChannelLayout.Depth,
            TextureFormat.DepthComponent24 => PixelChannelLayout.Depth,
            TextureFormat.DepthComponent32 => PixelChannelLayout.Depth,
            TextureFormat.DepthComponent32f => PixelChannelLayout.Depth,
            TextureFormat.Depth24Stencil8 => PixelChannelLayout.DepthStencil,
            TextureFormat.Depth32fStencil8 => PixelChannelLayout.DepthStencil,
            TextureFormat.StencilIndex8 => PixelChannelLayout.Stencil,
            _ => PixelChannelLayout.Rgba
        };
    }

    /// <summary>
    /// Gets the appropriate PixelComponentType for a given internal format.
    /// </summary>
    public static PixelComponentType GetPixelType(TextureFormat internalFormat)
    {
        return internalFormat switch
        {
            TextureFormat.R8ui or TextureFormat.Rg8ui or TextureFormat.Rgb8ui or TextureFormat.Rgba8ui => PixelComponentType.UnsignedByte,
            TextureFormat.R8i or TextureFormat.Rg8i or TextureFormat.Rgb8i or TextureFormat.Rgba8i => PixelComponentType.Byte,
            TextureFormat.R16i or TextureFormat.Rg16i or TextureFormat.Rgb16i or TextureFormat.Rgba16i => PixelComponentType.Short,
            TextureFormat.R16ui or TextureFormat.Rg16ui or TextureFormat.Rgb16ui or TextureFormat.Rgba16ui => PixelComponentType.UnsignedShort,
            TextureFormat.R32i or TextureFormat.Rg32i or TextureFormat.Rgb32i or TextureFormat.Rgba32i => PixelComponentType.Int,
            TextureFormat.Rgb32ui => PixelComponentType.UnsignedInt,
            TextureFormat.Rgb10A2ui => PixelComponentType.UnsignedInt2101010,
            TextureFormat.R32ui => PixelComponentType.UnsignedInt,
            TextureFormat.Rg32ui => PixelComponentType.UnsignedInt,
            TextureFormat.Rgba32ui => PixelComponentType.UnsignedInt,
            TextureFormat.Rgba16f => PixelComponentType.Half,
            TextureFormat.Rgba32f => PixelComponentType.Float,
            TextureFormat.Rgba16 => PixelComponentType.UnsignedShort,
            TextureFormat.Rgba8 => PixelComponentType.UnsignedByte,
            TextureFormat.Rgba => PixelComponentType.UnsignedByte,
            TextureFormat.R11fG11fB10f => PixelComponentType.Float,
            TextureFormat.Rgb16f => PixelComponentType.Half,
            TextureFormat.Rgb32f => PixelComponentType.Float,
            TextureFormat.Rgb16 => PixelComponentType.UnsignedShort,
            TextureFormat.Rgb8 => PixelComponentType.UnsignedByte,
            TextureFormat.Rgb => PixelComponentType.UnsignedByte,
            TextureFormat.Rg16f => PixelComponentType.Half,
            TextureFormat.Rg32f => PixelComponentType.Float,
            TextureFormat.Rg16 => PixelComponentType.UnsignedShort,
            TextureFormat.Rg8 => PixelComponentType.UnsignedByte,
            TextureFormat.R16f => PixelComponentType.Half,
            TextureFormat.R32f => PixelComponentType.Float,
            TextureFormat.R16 => PixelComponentType.UnsignedShort,
            TextureFormat.R8 => PixelComponentType.UnsignedByte,
            TextureFormat.DepthComponent => PixelComponentType.Float,
            TextureFormat.DepthComponent16 => PixelComponentType.UnsignedShort,
            TextureFormat.DepthComponent24 => PixelComponentType.UnsignedInt,
            TextureFormat.DepthComponent32 => PixelComponentType.UnsignedInt,
            TextureFormat.DepthComponent32f => PixelComponentType.Float,
            TextureFormat.Depth24Stencil8 => PixelComponentType.UnsignedInt248,
            TextureFormat.Depth32fStencil8 => PixelComponentType.Float32UnsignedInt248,
            TextureFormat.StencilIndex8 => PixelComponentType.UnsignedByte,
            _ => PixelComponentType.UnsignedByte
        };
    }

    /// <summary>
    /// Checks if the format is a depth or depth-stencil format.
    /// </summary>
    public static bool IsDepthFormat(TextureFormat internalFormat)
    {
        return internalFormat switch
        {
            TextureFormat.DepthComponent => true,
            TextureFormat.DepthComponent16 => true,
            TextureFormat.DepthComponent24 => true,
            TextureFormat.DepthComponent32 => true,
            TextureFormat.DepthComponent32f => true,
            TextureFormat.Depth24Stencil8 => true,
            TextureFormat.Depth32fStencil8 => true,
            _ => false
        };
    }

    /// <summary>Checks whether a texture has stencil-only storage.</summary>
    public static bool IsStencilFormat(TextureFormat internalFormat) => internalFormat == TextureFormat.StencilIndex8;

}
