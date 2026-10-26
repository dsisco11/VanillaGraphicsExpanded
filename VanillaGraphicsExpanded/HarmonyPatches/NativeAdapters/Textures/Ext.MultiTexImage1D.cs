using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.MultiTexImage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteExtMultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    #endregion
}
