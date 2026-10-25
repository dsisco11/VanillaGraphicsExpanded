using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.GenerateTextureMipmap overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void GenerateTextureMipmap(int @texture)
        => StateCache.Current.ExecuteCoreGenerateTextureMipmap(@texture);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void GenerateTextureMipmap(uint @texture)
        => StateCache.Current.ExecuteCoreGenerateTextureMipmap(@texture);

    #endregion
}
