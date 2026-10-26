using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext TextureImage1D execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);
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
