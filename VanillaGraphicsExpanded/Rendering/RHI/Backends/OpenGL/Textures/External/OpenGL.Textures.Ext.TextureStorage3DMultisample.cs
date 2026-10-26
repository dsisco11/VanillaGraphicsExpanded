using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext TextureStorage3DMultisample execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureStorage3DMultisample(int @texture, ExtDirectStateAccess @target, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureStorage3DMultisample(int @texture, ExtDirectStateAccess @target, int @samples, InternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureStorage3DMultisample(int @texture, ExtDirectStateAccess @target, int @samples, SizedInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureStorage3DMultisample(uint @texture, ExtDirectStateAccess @target, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureStorage3DMultisample(uint @texture, ExtDirectStateAccess @target, int @samples, InternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureStorage3DMultisample(uint @texture, ExtDirectStateAccess @target, int @samples, SizedInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorage3DMultisample(@texture, @target, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
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
