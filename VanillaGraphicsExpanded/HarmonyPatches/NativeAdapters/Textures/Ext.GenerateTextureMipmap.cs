using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.GenerateTextureMipmap overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void GenerateTextureMipmap(int @texture, TextureTarget @target)
        => OpenGLTexturesCalls.ExecuteExtGenerateTextureMipmap(@texture, @target);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void GenerateTextureMipmap(uint @texture, TextureTarget @target)
        => OpenGLTexturesCalls.ExecuteExtGenerateTextureMipmap(@texture, @target);

    #endregion
}
