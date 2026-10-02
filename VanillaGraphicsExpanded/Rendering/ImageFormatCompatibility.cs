using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Checks image-view formats against mutable texture storage's by-size compatibility rule.</summary>
internal static class ImageFormatCompatibility
{
    #region Public API
    /// <summary>Rejects unsupported image formats and storage/view texel-size disagreement before binding.</summary>
    internal static bool Matches(PixelInternalFormat storage, SizedInternalFormat view)
    {
        int bytes = Bytes(view);
        return bytes != 0 && bytes == Bytes((SizedInternalFormat)storage);
    }
    #endregion

    #region Private
    /// <summary>Maps the formats accepted by image load/store to their complete texel sizes.</summary>
    private static int Bytes(SizedInternalFormat format) => format switch
    {
        SizedInternalFormat.Rgba32f or SizedInternalFormat.Rgba32i or SizedInternalFormat.Rgba32ui => 16,
        SizedInternalFormat.Rgba16f or SizedInternalFormat.Rgba16 or SizedInternalFormat.Rgba16Snorm or
            SizedInternalFormat.Rgba16i or SizedInternalFormat.Rgba16ui or
            SizedInternalFormat.Rg32f or SizedInternalFormat.Rg32i or SizedInternalFormat.Rg32ui => 8,
        SizedInternalFormat.Rgba8 or SizedInternalFormat.Rgba8Snorm or SizedInternalFormat.Rgba8i or SizedInternalFormat.Rgba8ui or
            SizedInternalFormat.Rgb10A2 or SizedInternalFormat.Rgb10A2ui or SizedInternalFormat.R11fG11fB10f or
            SizedInternalFormat.Rg16f or SizedInternalFormat.Rg16 or SizedInternalFormat.Rg16Snorm or SizedInternalFormat.Rg16i or SizedInternalFormat.Rg16ui or
            SizedInternalFormat.R32f or SizedInternalFormat.R32i or SizedInternalFormat.R32ui => 4,
        SizedInternalFormat.Rg8 or SizedInternalFormat.Rg8Snorm or SizedInternalFormat.Rg8i or SizedInternalFormat.Rg8ui or
            SizedInternalFormat.R16f or SizedInternalFormat.R16 or SizedInternalFormat.R16Snorm or SizedInternalFormat.R16i or SizedInternalFormat.R16ui => 2,
        SizedInternalFormat.R8 or SizedInternalFormat.R8Snorm or SizedInternalFormat.R8i or SizedInternalFormat.R8ui => 1,
        _ => 0
    };
    #endregion
}
