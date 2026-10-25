using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext CompressedTextureImage2D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, IntPtr @bits)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, IntPtr @bits)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, ref T8 @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, ref @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,,] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, ref T8 @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, ref @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,,] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage2D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
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
