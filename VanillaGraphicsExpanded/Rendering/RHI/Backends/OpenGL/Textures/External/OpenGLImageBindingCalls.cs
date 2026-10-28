using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact external selectors without managed suppression or native success probes.</summary>
internal static class OpenGLImageBindingCalls
{
    #region Internal API

    #endregion

    #region Private
    /// <summary>Checks the core image-format domain through existing semantic format classification.</summary>
    internal static bool IsImageFormat(SizedInternalFormat format)
    {
        try
        {
            var semantic = OpenGLImageValues.Format((PixelInternalFormat)format);
            return ImageFormatCompatibility.Matches(semantic, semantic);
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
    #endregion
}
