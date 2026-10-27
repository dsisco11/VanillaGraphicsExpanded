using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Classifies specified view-compatible sized formats without querying individual textures.</summary>
internal static class TextureViewFormatCompatibility
{
    #region Internal API
    /// <summary>Accepts equal formats and formats belonging to the same specified compatibility class.</summary>
    internal static bool AreCompatible(TextureFormat source, TextureFormat target)
        => source == target || Class(source) is int group && group != 0 && group == Class(target);
    #endregion

    #region Private
    /// <summary>Returns the bit-width compatibility class; unclassified formats require an exact match.</summary>
    private static int Class(TextureFormat format) => format switch
    {
        TextureFormat.Rgba32f or TextureFormat.Rgba32ui or TextureFormat.Rgba32i => 128,
        TextureFormat.Rgb32f or TextureFormat.Rgb32ui or TextureFormat.Rgb32i => 96,
        TextureFormat.Rgba16f or TextureFormat.Rg32f or TextureFormat.Rgba16ui or TextureFormat.Rg32ui
            or TextureFormat.Rgba16i or TextureFormat.Rg32i or TextureFormat.Rgba16 or TextureFormat.Rgba16Snorm => 64,
        TextureFormat.Rgb16 or TextureFormat.Rgb16Snorm or TextureFormat.Rgb16f or TextureFormat.Rgb16ui or TextureFormat.Rgb16i => 48,
        TextureFormat.Rg16f or TextureFormat.R11fG11fB10f or TextureFormat.R32f or TextureFormat.Rgb10A2ui
            or TextureFormat.Rgba8ui or TextureFormat.Rg16ui or TextureFormat.R32ui or TextureFormat.Rgba8i
            or TextureFormat.Rg16i or TextureFormat.R32i or TextureFormat.Rgb10A2 or TextureFormat.Rgba8
            or TextureFormat.Rg16 or TextureFormat.Rgba8Snorm or TextureFormat.Rg16Snorm
            or TextureFormat.Srgb8Alpha8 or TextureFormat.Rgb9E5 => 32,
        TextureFormat.Rgb8 or TextureFormat.Rgb8Snorm or TextureFormat.Srgb8 or TextureFormat.Rgb8ui or TextureFormat.Rgb8i => 24,
        TextureFormat.R16f or TextureFormat.Rg8ui or TextureFormat.R16ui or TextureFormat.Rg8i
            or TextureFormat.R16i or TextureFormat.Rg8 or TextureFormat.R16 or TextureFormat.Rg8Snorm or TextureFormat.R16Snorm => 16,
        TextureFormat.R8ui or TextureFormat.R8i or TextureFormat.R8 or TextureFormat.R8Snorm => 8,
        TextureFormat.CompressedRgbS3tcDxt1Ext or TextureFormat.CompressedSrgbS3tcDxt1Ext => 1001,
        TextureFormat.CompressedRgbaS3tcDxt1Ext or TextureFormat.CompressedSrgbAlphaS3tcDxt1Ext => 1002,
        TextureFormat.CompressedRgbaS3tcDxt3Ext or TextureFormat.CompressedSrgbAlphaS3tcDxt3Ext => 1003,
        TextureFormat.CompressedRgbaS3tcDxt5Ext or TextureFormat.CompressedSrgbAlphaS3tcDxt5Ext => 1004,
        TextureFormat.CompressedRedRgtc1 or TextureFormat.CompressedSignedRedRgtc1 => 1005,
        TextureFormat.CompressedRgRgtc2 or TextureFormat.CompressedSignedRgRgtc2 => 1006,
        TextureFormat.CompressedRgbaBptcUnorm or TextureFormat.CompressedSrgbAlphaBptcUnorm => 1007,
        TextureFormat.CompressedRgbBptcSignedFloat or TextureFormat.CompressedRgbBptcUnsignedFloat => 1008,
        _ => 0
    };
    #endregion
}
