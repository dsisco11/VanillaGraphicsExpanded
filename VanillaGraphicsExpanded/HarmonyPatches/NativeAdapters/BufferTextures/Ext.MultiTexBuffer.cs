using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.Ext.MultiTexBuffer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexBuffer(TextureUnit @texunit, TextureTarget @target, InternalFormat @internalformat, int @buffer)
        => OpenGLBufferTexturesCalls.ExecuteExtMultiTexBuffer(@texunit, @target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexBuffer(TextureUnit @texunit, TextureTarget @target, InternalFormat @internalformat, uint @buffer)
        => OpenGLBufferTexturesCalls.ExecuteExtMultiTexBuffer(@texunit, @target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexBuffer(TextureUnit @texunit, TextureTarget @target, TypeEnum @internalformat, int @buffer)
        => OpenGLBufferTexturesCalls.ExecuteExtMultiTexBuffer(@texunit, @target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexBuffer(TextureUnit @texunit, TextureTarget @target, TypeEnum @internalformat, uint @buffer)
        => OpenGLBufferTexturesCalls.ExecuteExtMultiTexBuffer(@texunit, @target, @internalformat, @buffer);

    #endregion
}
