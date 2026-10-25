using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexParameter overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, int @param)
        => StateCache.Current.ExecuteCoreTexParameter(@target, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, int[] @params)
        => StateCache.Current.ExecuteCoreTexParameter(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, int* @params)
        => StateCache.Current.ExecuteCoreTexParameter(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, float @param)
        => StateCache.Current.ExecuteCoreTexParameter(@target, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, float[] @params)
        => StateCache.Current.ExecuteCoreTexParameter(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, float* @params)
        => StateCache.Current.ExecuteCoreTexParameter(@target, @pname, @params);

    #endregion
}
