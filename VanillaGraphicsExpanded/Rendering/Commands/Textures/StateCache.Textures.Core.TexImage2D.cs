using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TexImage2D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage2D(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage2D(@target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage2D<T8>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage2D<T8>(@target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage2D<T8>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage2D<T8>(@target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage2D<T8>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage2D<T8>(@target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage2D<T8>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage2D<T8>(@target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
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
