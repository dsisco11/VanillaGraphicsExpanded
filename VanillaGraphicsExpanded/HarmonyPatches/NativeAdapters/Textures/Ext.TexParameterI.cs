using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexParameterI overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, ref int @params)
        => StateCache.Current.ExecuteExtTexParameterI(@target, @pname, ref @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, int[] @params)
        => StateCache.Current.ExecuteExtTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, int* @params)
        => StateCache.Current.ExecuteExtTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, ref uint @params)
        => StateCache.Current.ExecuteExtTexParameterI(@target, @pname, ref @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, uint[] @params)
        => StateCache.Current.ExecuteExtTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, uint* @params)
        => StateCache.Current.ExecuteExtTexParameterI(@target, @pname, @params);

    #endregion
}
