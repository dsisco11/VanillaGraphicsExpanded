using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolves only unsized image formats when typed consumers need the driver's chosen representation.</summary>
internal static class GpuTextureFormatReader
{
    #region Internal API
    /// <summary>Distinguishes explicit storage formats from requests whose representation is driver-selected.</summary>
    internal static bool IsExplicitFormat(PixelInternalFormat supplied)
        => supplied is not ((PixelInternalFormat)All.Red or (PixelInternalFormat)All.Rg or PixelInternalFormat.Rgb
            or PixelInternalFormat.Rgba or PixelInternalFormat.DepthComponent or PixelInternalFormat.DepthStencil
            or PixelInternalFormat.CompressedRed or PixelInternalFormat.CompressedRg
            or PixelInternalFormat.CompressedRgb or PixelInternalFormat.CompressedRgba
            or PixelInternalFormat.CompressedSrgb or PixelInternalFormat.CompressedSrgbAlpha);


    #endregion
}
