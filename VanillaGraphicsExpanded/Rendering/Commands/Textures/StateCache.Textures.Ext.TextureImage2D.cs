using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TextureImage2D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);
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
