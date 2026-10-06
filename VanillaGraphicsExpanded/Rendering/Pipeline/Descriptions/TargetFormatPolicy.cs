using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Bounded sized-color format policy for complete target signatures.</summary>
internal static class TargetFormatPolicy
{
    #region Public API
    /// <summary>Accepts sized normalized, floating and integer formats; never guesses unsized metadata.</summary>
    public static bool IsColor(PixelInternalFormat format) => format is
        PixelInternalFormat.R8 or PixelInternalFormat.R16 or PixelInternalFormat.R16f or PixelInternalFormat.R32f or
        PixelInternalFormat.Rg8 or PixelInternalFormat.Rg16 or PixelInternalFormat.Rg16f or PixelInternalFormat.Rg32f or
        PixelInternalFormat.Rgb8 or PixelInternalFormat.Rgb16 or PixelInternalFormat.Rgb16f or PixelInternalFormat.Rgb32f or
        PixelInternalFormat.Rgba8 or PixelInternalFormat.Rgba16 or PixelInternalFormat.Rgba16f or PixelInternalFormat.Rgba32f or
        PixelInternalFormat.Srgb8 or PixelInternalFormat.Srgb8Alpha8 || IsInteger(format);

    /// <summary>Identifies integer attachments where blending is unsupported by the complete contract.</summary>
    public static bool IsInteger(PixelInternalFormat format) => format is
        PixelInternalFormat.R8i or PixelInternalFormat.R16i or PixelInternalFormat.R32i or
        PixelInternalFormat.R8ui or PixelInternalFormat.R16ui or PixelInternalFormat.R32ui or
        PixelInternalFormat.Rg8i or PixelInternalFormat.Rg16i or PixelInternalFormat.Rg32i or
        PixelInternalFormat.Rg8ui or PixelInternalFormat.Rg16ui or PixelInternalFormat.Rg32ui or
        PixelInternalFormat.Rgb8i or PixelInternalFormat.Rgb16i or PixelInternalFormat.Rgb32i or
        PixelInternalFormat.Rgb8ui or PixelInternalFormat.Rgb16ui or PixelInternalFormat.Rgb32ui or
        PixelInternalFormat.Rgba8i or PixelInternalFormat.Rgba16i or PixelInternalFormat.Rgba32i or
        PixelInternalFormat.Rgba8ui or PixelInternalFormat.Rgba16ui or PixelInternalFormat.Rgba32ui or PixelInternalFormat.Rgb10A2ui;
    #endregion
}
