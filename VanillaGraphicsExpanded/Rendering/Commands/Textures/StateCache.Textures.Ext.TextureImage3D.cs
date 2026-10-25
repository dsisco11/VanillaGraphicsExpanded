using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TextureImage3D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T10 @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,,] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T10 @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,,] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T10 @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,,] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T10 @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,,] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[] @pixels)
        where T10 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.TextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);
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
