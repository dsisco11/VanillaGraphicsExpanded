using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Bounded sized-color format policy for complete target signatures.</summary>
internal static class TargetFormatPolicy
{
    #region Public API
    /// <summary>Accepts sized normalized, floating and integer formats; never guesses unsized metadata.</summary>
    public static bool IsColor(TextureFormat format) => format is
        TextureFormat.R8 or TextureFormat.R16 or TextureFormat.R16f or TextureFormat.R32f or
        TextureFormat.Rg8 or TextureFormat.Rg16 or TextureFormat.Rg16f or TextureFormat.Rg32f or
        TextureFormat.R11fG11fB10f or TextureFormat.Rgb8 or TextureFormat.Rgb16 or TextureFormat.Rgb16f or TextureFormat.Rgb32f or
        TextureFormat.Rgba8 or TextureFormat.Rgba16 or TextureFormat.Rgba16f or TextureFormat.Rgba32f or
        TextureFormat.Srgb8 or TextureFormat.Srgb8Alpha8 || IsInteger(format);

    /// <summary>Identifies integer attachments where blending is unsupported by the complete contract.</summary>
    public static bool IsInteger(TextureFormat format) => format is
        TextureFormat.R8i or TextureFormat.R16i or TextureFormat.R32i or
        TextureFormat.R8ui or TextureFormat.R16ui or TextureFormat.R32ui or
        TextureFormat.Rg8i or TextureFormat.Rg16i or TextureFormat.Rg32i or
        TextureFormat.Rg8ui or TextureFormat.Rg16ui or TextureFormat.Rg32ui or
        TextureFormat.Rgb8i or TextureFormat.Rgb16i or TextureFormat.Rgb32i or
        TextureFormat.Rgb8ui or TextureFormat.Rgb16ui or TextureFormat.Rgb32ui or
        TextureFormat.Rgba8i or TextureFormat.Rgba16i or TextureFormat.Rgba32i or
        TextureFormat.Rgba8ui or TextureFormat.Rgba16ui or TextureFormat.Rgba32ui or TextureFormat.Rgb10A2ui;
    #endregion
}
