using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext EGLImageTargetTextureStorage execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtEGLImageTargetTextureStorage(int @texture, IntPtr @image, ref int @attrib_list)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.EGLImageTargetTextureStorage(@texture, @image, ref @attrib_list);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtEGLImageTargetTextureStorage(int @texture, IntPtr @image, int[] @attrib_list)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.EGLImageTargetTextureStorage(@texture, @image, @attrib_list);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtEGLImageTargetTextureStorage(int @texture, IntPtr @image, int* @attrib_list)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.EGLImageTargetTextureStorage(@texture, @image, @attrib_list);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtEGLImageTargetTextureStorage(uint @texture, IntPtr @image, ref int @attrib_list)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.EGLImageTargetTextureStorage(@texture, @image, ref @attrib_list);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtEGLImageTargetTextureStorage(uint @texture, IntPtr @image, int[] @attrib_list)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.EGLImageTargetTextureStorage(@texture, @image, @attrib_list);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtEGLImageTargetTextureStorage(uint @texture, IntPtr @image, int* @attrib_list)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.EGLImageTargetTextureStorage(@texture, @image, @attrib_list);
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
