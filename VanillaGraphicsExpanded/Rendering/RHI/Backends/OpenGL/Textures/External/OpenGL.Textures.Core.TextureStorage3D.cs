using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core TextureStorage3D execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureStorage3D(int @texture, int @levels, InternalFormat @internalformat, int @width, int @height, int @depth)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureStorage3D(@texture, @levels, @internalformat, @width, @height, @depth);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureStorage3D(int @texture, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureStorage3D(@texture, @levels, @internalformat, @width, @height, @depth);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureStorage3D(uint @texture, int @levels, InternalFormat @internalformat, int @width, int @height, int @depth)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureStorage3D(@texture, @levels, @internalformat, @width, @height, @depth);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureStorage3D(uint @texture, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureStorage3D(@texture, @levels, @internalformat, @width, @height, @depth);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }
    #endregion
}
