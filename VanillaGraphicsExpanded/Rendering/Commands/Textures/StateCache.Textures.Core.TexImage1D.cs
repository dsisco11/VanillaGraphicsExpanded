using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TexImage1D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage1D(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage1D(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T7 @pixels)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[,,] @pixels)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[,] @pixels)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[] @pixels)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.TexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);
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
