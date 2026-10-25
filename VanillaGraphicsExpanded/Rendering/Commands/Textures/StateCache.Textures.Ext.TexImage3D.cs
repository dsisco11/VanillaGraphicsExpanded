using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TexImage3D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexImage3D(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TexImage3D(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexImage3D<T9>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexImage3D<T9>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexImage3D<T9>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexImage3D<T9>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
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
