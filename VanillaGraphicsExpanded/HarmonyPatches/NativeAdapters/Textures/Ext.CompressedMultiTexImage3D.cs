using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CompressedMultiTexImage3D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage3D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, IntPtr @bits)
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage3D(@texunit, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage3D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, ref T9 @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage3D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage3D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[,,] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage3D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage3D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[,] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage3D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage3D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage3D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    #endregion
}
