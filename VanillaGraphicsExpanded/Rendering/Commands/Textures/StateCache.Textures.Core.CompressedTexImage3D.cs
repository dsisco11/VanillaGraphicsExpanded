using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core CompressedTexImage3D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreCompressedTexImage3D(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, IntPtr @data)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.CompressedTexImage3D(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreCompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, ref T8 @data)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.CompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, ref @data);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreCompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[,,] @data)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.CompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreCompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[,] @data)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.CompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreCompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[] @data)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.CompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);
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
