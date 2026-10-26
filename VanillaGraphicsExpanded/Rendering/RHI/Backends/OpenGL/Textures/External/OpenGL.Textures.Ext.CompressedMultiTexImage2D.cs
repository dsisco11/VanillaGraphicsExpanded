using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext CompressedMultiTexImage2D execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtCompressedMultiTexImage2D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, IntPtr @bits)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage2D(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtCompressedMultiTexImage2D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, ref T8 @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage2D<T8>(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, ref @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtCompressedMultiTexImage2D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,,] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage2D<T8>(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtCompressedMultiTexImage2D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage2D<T8>(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtCompressedMultiTexImage2D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[] @bits)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CompressedMultiTexImage2D<T8>(@texunit, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);
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
