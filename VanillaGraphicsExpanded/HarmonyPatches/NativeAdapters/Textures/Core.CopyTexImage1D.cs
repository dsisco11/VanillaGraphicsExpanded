using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.CopyTexImage1D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CopyTexImage1D(TextureTarget @target, int @level, InternalFormat @internalformat, int @x, int @y, int @width, int @border)
        => OpenGLTexturesCalls.ExecuteCoreCopyTexImage1D(@target, @level, @internalformat, @x, @y, @width, @border);

    #endregion
}
