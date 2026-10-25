using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.MultiTexImage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => StateCache.Current.ExecuteExtMultiTexImage2D(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => StateCache.Current.ExecuteExtMultiTexImage2D(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtMultiTexImage2D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtMultiTexImage2D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtMultiTexImage2D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtMultiTexImage2D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtMultiTexImage2D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtMultiTexImage2D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtMultiTexImage2D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexImage2D<T9>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtMultiTexImage2D<T9>(@texunit, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    #endregion
}
