using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexStorage3D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexStorage3D(TextureTarget3d @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteCoreTexStorage3D(@target, @levels, @internalformat, @width, @height, @depth);

    #endregion
}
