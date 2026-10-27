using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolves only unsized image formats when typed consumers need the driver's chosen representation.</summary>
internal static class GpuTextureFormatReader
{
    #region Internal API
    /// <summary>Distinguishes explicit storage formats from requests whose representation is driver-selected.</summary>
    internal static bool IsExplicitFormat(TextureFormat supplied)
        => supplied is not (TextureFormat.Red or TextureFormat.Rg or TextureFormat.Rgb
            or TextureFormat.Rgba or TextureFormat.DepthComponent or TextureFormat.DepthStencil
            or TextureFormat.CompressedRed or TextureFormat.CompressedRg
            or TextureFormat.CompressedRgb or TextureFormat.CompressedRgba
            or TextureFormat.CompressedSrgb or TextureFormat.CompressedSrgbAlpha);


    #endregion
}
