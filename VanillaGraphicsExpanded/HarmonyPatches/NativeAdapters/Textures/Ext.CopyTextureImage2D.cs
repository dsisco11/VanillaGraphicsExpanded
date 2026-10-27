using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CopyTextureImage2D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CopyTextureImage2D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @x, int @y, int @width, int @height, int @border)
        => OpenGLTexturesCalls.ExecuteExtCopyTextureImage2D(@texture, @target, @level, @internalformat, @x, @y, @width, @height, @border);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CopyTextureImage2D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @x, int @y, int @width, int @height, int @border)
        => OpenGLTexturesCalls.ExecuteExtCopyTextureImage2D(@texture, @target, @level, @internalformat, @x, @y, @width, @height, @border);

    #endregion
}
