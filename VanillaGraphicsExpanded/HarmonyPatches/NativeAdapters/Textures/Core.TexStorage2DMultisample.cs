using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexStorage2DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorage2DMultisample(TextureTargetMultisample2d @target, int @samples, SizedInternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteCoreTexStorage2DMultisample(@target, @samples, @internalformat, @width, @height, @fixedsamplelocations);

    #endregion
}
