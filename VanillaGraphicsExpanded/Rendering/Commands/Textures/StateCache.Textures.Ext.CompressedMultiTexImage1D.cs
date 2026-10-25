using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext CompressedMultiTexImage1D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedMultiTexImage1D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, IntPtr @bits)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage1D(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedMultiTexImage1D<T7>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, ref T7 @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage1D<T7>(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, ref @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedMultiTexImage1D<T7>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,,] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage1D<T7>(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedMultiTexImage1D<T7>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage1D<T7>(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCompressedMultiTexImage1D<T7>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[] @bits)
        where T7 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage1D<T7>(@texunit, @target, @level, @internalformat, @width, @border, @imageSize, @bits);
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
