using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.GenerateMultiTexMipmap overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void GenerateMultiTexMipmap(TextureUnit @texunit, TextureTarget @target)
        => OpenGLTexturesCalls.ExecuteExtGenerateMultiTexMipmap(@texunit, @target);

    #endregion
}
