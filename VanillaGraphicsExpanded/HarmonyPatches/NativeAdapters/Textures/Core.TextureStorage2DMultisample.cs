using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TextureStorage2DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2DMultisample(int @texture, int @samples, InternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteCoreTextureStorage2DMultisample(@texture, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2DMultisample(int @texture, int @samples, SizedInternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteCoreTextureStorage2DMultisample(@texture, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2DMultisample(uint @texture, int @samples, InternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteCoreTextureStorage2DMultisample(@texture, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2DMultisample(uint @texture, int @samples, SizedInternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteCoreTextureStorage2DMultisample(@texture, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    #endregion
}
