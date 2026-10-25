using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Classifies specified view-compatible sized formats without querying individual textures.</summary>
internal static class TextureViewFormatCompatibility
{
    #region Internal API
    /// <summary>Accepts equal formats and formats belonging to the same specified compatibility class.</summary>
    internal static bool AreCompatible(PixelInternalFormat source, PixelInternalFormat target)
        => source == target || Class(source) is int group && group != 0 && group == Class(target);
    #endregion

    #region Private
    /// <summary>Returns the bit-width compatibility class; unclassified formats require an exact match.</summary>
    private static int Class(PixelInternalFormat format) => format switch
    {
        PixelInternalFormat.Rgba32f or PixelInternalFormat.Rgba32ui or PixelInternalFormat.Rgba32i => 128,
        PixelInternalFormat.Rgb32f or PixelInternalFormat.Rgb32ui or PixelInternalFormat.Rgb32i => 96,
        PixelInternalFormat.Rgba16f or PixelInternalFormat.Rg32f or PixelInternalFormat.Rgba16ui or PixelInternalFormat.Rg32ui
            or PixelInternalFormat.Rgba16i or PixelInternalFormat.Rg32i or PixelInternalFormat.Rgba16 or PixelInternalFormat.Rgba16Snorm => 64,
        PixelInternalFormat.Rgb16 or PixelInternalFormat.Rgb16Snorm or PixelInternalFormat.Rgb16f or PixelInternalFormat.Rgb16ui or PixelInternalFormat.Rgb16i => 48,
        PixelInternalFormat.Rg16f or PixelInternalFormat.R11fG11fB10f or PixelInternalFormat.R32f or PixelInternalFormat.Rgb10A2ui
            or PixelInternalFormat.Rgba8ui or PixelInternalFormat.Rg16ui or PixelInternalFormat.R32ui or PixelInternalFormat.Rgba8i
            or PixelInternalFormat.Rg16i or PixelInternalFormat.R32i or PixelInternalFormat.Rgb10A2 or PixelInternalFormat.Rgba8
            or PixelInternalFormat.Rg16 or PixelInternalFormat.Rgba8Snorm or PixelInternalFormat.Rg16Snorm
            or PixelInternalFormat.Srgb8Alpha8 or PixelInternalFormat.Rgb9E5 => 32,
        PixelInternalFormat.Rgb8 or PixelInternalFormat.Rgb8Snorm or PixelInternalFormat.Srgb8 or PixelInternalFormat.Rgb8ui or PixelInternalFormat.Rgb8i => 24,
        PixelInternalFormat.R16f or PixelInternalFormat.Rg8ui or PixelInternalFormat.R16ui or PixelInternalFormat.Rg8i
            or PixelInternalFormat.R16i or PixelInternalFormat.Rg8 or PixelInternalFormat.R16 or PixelInternalFormat.Rg8Snorm or PixelInternalFormat.R16Snorm => 16,
        PixelInternalFormat.R8ui or PixelInternalFormat.R8i or PixelInternalFormat.R8 or PixelInternalFormat.R8Snorm => 8,
        PixelInternalFormat.CompressedRgbS3tcDxt1Ext or PixelInternalFormat.CompressedSrgbS3tcDxt1Ext => 1001,
        PixelInternalFormat.CompressedRgbaS3tcDxt1Ext or PixelInternalFormat.CompressedSrgbAlphaS3tcDxt1Ext => 1002,
        PixelInternalFormat.CompressedRgbaS3tcDxt3Ext or PixelInternalFormat.CompressedSrgbAlphaS3tcDxt3Ext => 1003,
        PixelInternalFormat.CompressedRgbaS3tcDxt5Ext or PixelInternalFormat.CompressedSrgbAlphaS3tcDxt5Ext => 1004,
        PixelInternalFormat.CompressedRedRgtc1 or PixelInternalFormat.CompressedSignedRedRgtc1 => 1005,
        PixelInternalFormat.CompressedRgRgtc2 or PixelInternalFormat.CompressedSignedRgRgtc2 => 1006,
        PixelInternalFormat.CompressedRgbaBptcUnorm or PixelInternalFormat.CompressedSrgbAlphaBptcUnorm => 1007,
        PixelInternalFormat.CompressedRgbBptcSignedFloat or PixelInternalFormat.CompressedRgbBptcUnsignedFloat => 1008,
        _ => 0
    };
    #endregion
}
