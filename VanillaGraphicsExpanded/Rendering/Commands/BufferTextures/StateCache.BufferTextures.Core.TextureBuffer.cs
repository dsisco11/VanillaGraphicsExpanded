using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TextureBuffer execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBuffer(int @texture, InternalFormat @internalformat, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.TextureBuffer(@texture, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBuffer(int @texture, SizedInternalFormat @internalformat, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.TextureBuffer(@texture, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBuffer(uint @texture, InternalFormat @internalformat, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.TextureBuffer(@texture, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBuffer(uint @texture, SizedInternalFormat @internalformat, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.TextureBuffer(@texture, @internalformat, @buffer);
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
