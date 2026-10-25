using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorage2DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage2DMultisample(int @texture, TextureTarget @target, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteExtTextureStorage2DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage2DMultisample(int @texture, TextureTarget @target, int @samples, InternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteExtTextureStorage2DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage2DMultisample(int @texture, TextureTarget @target, int @samples, SizedInternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteExtTextureStorage2DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage2DMultisample(uint @texture, TextureTarget @target, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteExtTextureStorage2DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage2DMultisample(uint @texture, TextureTarget @target, int @samples, InternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteExtTextureStorage2DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage2DMultisample(uint @texture, TextureTarget @target, int @samples, SizedInternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteExtTextureStorage2DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    #endregion
}
