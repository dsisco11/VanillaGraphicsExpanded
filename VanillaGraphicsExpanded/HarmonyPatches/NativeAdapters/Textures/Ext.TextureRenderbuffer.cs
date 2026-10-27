using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureRenderbuffer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureRenderbuffer(int @texture, TextureTarget @target, int @renderbuffer)
        => OpenGLTexturesCalls.ExecuteExtTextureRenderbuffer(@texture, @target, @renderbuffer);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureRenderbuffer(uint @texture, TextureTarget @target, uint @renderbuffer)
        => OpenGLTexturesCalls.ExecuteExtTextureRenderbuffer(@texture, @target, @renderbuffer);

    #endregion
}
