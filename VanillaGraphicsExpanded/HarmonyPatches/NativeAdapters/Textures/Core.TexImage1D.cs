using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexImage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage1D(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => StateCache.Current.ExecuteCoreTexImage1D(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T7 @pixels)
        where T7 : struct
        => StateCache.Current.ExecuteCoreTexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[,,] @pixels)
        where T7 : struct
        => StateCache.Current.ExecuteCoreTexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[,] @pixels)
        where T7 : struct
        => StateCache.Current.ExecuteCoreTexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[] @pixels)
        where T7 : struct
        => StateCache.Current.ExecuteCoreTexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    #endregion
}
