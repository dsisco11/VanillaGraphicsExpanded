using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TextureBuffer execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureBuffer(int @texture, TextureTarget @target, ExtDirectStateAccess @internalformat, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), unchecked((int)@target), 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.TextureBuffer(@texture, @target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureBuffer(int @texture, TextureTarget @target, InternalFormat @internalformat, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), unchecked((int)@target), 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.TextureBuffer(@texture, @target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureBuffer(int @texture, TextureTarget @target, SizedInternalFormat @internalformat, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), unchecked((int)@target), 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.TextureBuffer(@texture, @target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureBuffer(uint @texture, TextureTarget @target, ExtDirectStateAccess @internalformat, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), unchecked((int)@target), 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.TextureBuffer(@texture, @target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureBuffer(uint @texture, TextureTarget @target, InternalFormat @internalformat, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), unchecked((int)@target), 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.TextureBuffer(@texture, @target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureBuffer(uint @texture, TextureTarget @target, SizedInternalFormat @internalformat, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(unchecked((int)@texture), unchecked((int)@target), 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.TextureBuffer(@texture, @target, @internalformat, @buffer);
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
