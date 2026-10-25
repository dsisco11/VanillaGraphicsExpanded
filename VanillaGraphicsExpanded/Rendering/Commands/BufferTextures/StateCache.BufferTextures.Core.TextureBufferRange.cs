using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TextureBufferRange execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBufferRange(int @texture, InternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBufferRange(int @texture, InternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBufferRange(int @texture, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBufferRange(int @texture, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBufferRange(uint @texture, InternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBufferRange(uint @texture, InternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBufferRange(uint @texture, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureBufferRange(uint @texture, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), (int)TextureTarget.TextureBuffer, 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);
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
