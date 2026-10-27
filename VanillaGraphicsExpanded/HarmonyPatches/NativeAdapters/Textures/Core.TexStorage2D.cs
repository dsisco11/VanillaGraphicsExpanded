using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexStorage2D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexStorage2D(TextureTarget2d @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteCoreTexStorage2D(@target, @levels, @internalformat, @width, @height);

    #endregion
}
