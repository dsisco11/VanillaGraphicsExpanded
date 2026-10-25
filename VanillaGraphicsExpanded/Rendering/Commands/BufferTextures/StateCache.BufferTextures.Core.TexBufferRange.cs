using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TexBufferRange execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexBufferRange(TextureBufferTarget @target, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), 0, false, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TexBufferRange(@target, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexBufferRange(TextureBufferTarget @target, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), 0, false, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TexBufferRange(@target, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexBufferRange(TextureBufferTarget @target, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), 0, false, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TexBufferRange(@target, @internalformat, @buffer, @offset, @size);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexBufferRange(TextureBufferTarget @target, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), 0, false, false, unchecked((int)@buffer), unchecked((int)@internalformat), unchecked((nint)@offset), unchecked((nint)@size), false);
        bool returned = false;
        try
        {
            GL.TexBufferRange(@target, @internalformat, @buffer, @offset, @size);
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
