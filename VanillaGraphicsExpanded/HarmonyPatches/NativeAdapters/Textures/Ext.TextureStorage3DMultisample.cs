using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorage3DMultisample overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3DMultisample(int @texture, ExtDirectStateAccess @target, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3DMultisample(int @texture, ExtDirectStateAccess @target, int @samples, InternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3DMultisample(int @texture, ExtDirectStateAccess @target, int @samples, SizedInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3DMultisample(uint @texture, ExtDirectStateAccess @target, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3DMultisample(uint @texture, ExtDirectStateAccess @target, int @samples, InternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3DMultisample(uint @texture, ExtDirectStateAccess @target, int @samples, SizedInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    #endregion
}
