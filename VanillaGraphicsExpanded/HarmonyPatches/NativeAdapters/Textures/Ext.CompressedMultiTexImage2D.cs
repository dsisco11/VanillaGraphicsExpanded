using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CompressedMultiTexImage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage2D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, IntPtr @bits)
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage2D(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage2D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, ref T8 @bits)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage2D<T8>(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage2D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,,] @bits)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage2D<T8>(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage2D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,] @bits)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage2D<T8>(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage2D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[] @bits)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage2D<T8>(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    #endregion
}
