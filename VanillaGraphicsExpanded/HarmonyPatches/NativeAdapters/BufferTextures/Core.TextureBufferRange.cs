using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.TextureBufferRange overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureBufferRange(int @texture, InternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureBufferRange(int @texture, InternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureBufferRange(int @texture, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureBufferRange(int @texture, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureBufferRange(uint @texture, InternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureBufferRange(uint @texture, InternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureBufferRange(uint @texture, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureBufferRange(uint @texture, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
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
