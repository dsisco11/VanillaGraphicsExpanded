using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexImage3DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage3DMultisample(TextureTargetMultisample @target, int @samples, PixelInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteCoreTexImage3DMultisample(@target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    #endregion
}
