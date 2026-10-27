using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Checks image-view formats against mutable texture storage's by-size compatibility rule.</summary>
internal static class ImageFormatCompatibility
{
    #region Public API
    /// <summary>Rejects unsupported image formats and storage/view texel-size disagreement before binding.</summary>
    internal static bool Matches(TextureFormat storage, TextureFormat view)
    {
        int bytes = Bytes(view);
        return bytes != 0 && bytes == Bytes(storage);
    }
    #endregion

    #region Private
    /// <summary>Maps the formats accepted by image load/store to their complete texel sizes.</summary>
    private static int Bytes(TextureFormat format) => format switch
    {
        TextureFormat.Rgba32f or TextureFormat.Rgba32i or TextureFormat.Rgba32ui => 16,
        TextureFormat.Rgba16f or TextureFormat.Rgba16 or TextureFormat.Rgba16Snorm or
            TextureFormat.Rgba16i or TextureFormat.Rgba16ui or
            TextureFormat.Rg32f or TextureFormat.Rg32i or TextureFormat.Rg32ui => 8,
        TextureFormat.Rgba8 or TextureFormat.Rgba8Snorm or TextureFormat.Rgba8i or TextureFormat.Rgba8ui or
            TextureFormat.Rgb10A2 or TextureFormat.Rgb10A2ui or TextureFormat.R11fG11fB10f or
            TextureFormat.Rg16f or TextureFormat.Rg16 or TextureFormat.Rg16Snorm or TextureFormat.Rg16i or TextureFormat.Rg16ui or
            TextureFormat.R32f or TextureFormat.R32i or TextureFormat.R32ui => 4,
        TextureFormat.Rg8 or TextureFormat.Rg8Snorm or TextureFormat.Rg8i or TextureFormat.Rg8ui or
            TextureFormat.R16f or TextureFormat.R16 or TextureFormat.R16Snorm or TextureFormat.R16i or TextureFormat.R16ui => 2,
        TextureFormat.R8 or TextureFormat.R8Snorm or TextureFormat.R8i or TextureFormat.R8ui => 1,
        _ => 0
    };
    #endregion
}
