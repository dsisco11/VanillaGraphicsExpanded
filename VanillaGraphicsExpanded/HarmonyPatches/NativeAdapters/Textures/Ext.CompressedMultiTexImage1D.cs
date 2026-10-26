using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CompressedMultiTexImage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage1D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, IntPtr @bits)
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage1D(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage1D<T7>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, ref T7 @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage1D<T7>(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage1D<T7>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,,] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage1D<T7>(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage1D<T7>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage1D<T7>(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedMultiTexImage1D<T7>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedMultiTexImage1D<T7>(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    #endregion
}
