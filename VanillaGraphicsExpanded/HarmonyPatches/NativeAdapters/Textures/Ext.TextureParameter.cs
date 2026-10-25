using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureParameter overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int @param)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int[] @params)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int* @params)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float @param)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float[] @params)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float* @params)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int @param)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int[] @params)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int* @params)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float @param)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float[] @params)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float* @params)
        => StateCache.Current.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    #endregion
}
