using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext CompressedTextureImage1D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, IntPtr @bits)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, IntPtr @bits)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D<T7>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, ref T7 @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, ref @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D<T7>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,,] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D<T7>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D<T7>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D<T7>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, ref T7 @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, ref @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D<T7>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,,] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D<T7>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedTextureImage1D<T7>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
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
