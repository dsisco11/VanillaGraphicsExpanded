using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext MultiTexImage1D execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexImage1D<T8>(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexImage1D<T8>(@texunit, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
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
