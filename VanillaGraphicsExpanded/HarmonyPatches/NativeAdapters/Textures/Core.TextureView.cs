using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TextureView overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureView(int @texture, TextureTarget @target, int @origtexture, PixelInternalFormat @internalformat, int @minlevel, int @numlevels, int @minlayer, int @numlayers)
        => OpenGLTexturesCalls.ExecuteCoreTextureView(@texture, @target, @origtexture, @internalformat, @minlevel, @numlevels, @minlayer, @numlayers);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureView(uint @texture, TextureTarget @target, uint @origtexture, PixelInternalFormat @internalformat, uint @minlevel, uint @numlevels, uint @minlayer, uint @numlayers)
        => OpenGLTexturesCalls.ExecuteCoreTextureView(@texture, @target, @origtexture, @internalformat, @minlevel, @numlevels, @minlayer, @numlayers);

    #endregion
}
