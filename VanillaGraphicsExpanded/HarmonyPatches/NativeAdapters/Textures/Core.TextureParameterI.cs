using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TextureParameterI overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(int @texture, TextureParameterName @pname, ref int @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, ref @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(int @texture, TextureParameterName @pname, int[] @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(int @texture, TextureParameterName @pname, int* @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(uint @texture, TextureParameterName @pname, ref int @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, ref @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(uint @texture, TextureParameterName @pname, int[] @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(uint @texture, TextureParameterName @pname, int* @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(uint @texture, TextureParameterName @pname, ref uint @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, ref @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(uint @texture, TextureParameterName @pname, uint[] @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameterI(uint @texture, TextureParameterName @pname, uint* @params)
        => StateCache.Current.ExecuteCoreTextureParameterI(@texture, @pname, @params);

    #endregion
}
