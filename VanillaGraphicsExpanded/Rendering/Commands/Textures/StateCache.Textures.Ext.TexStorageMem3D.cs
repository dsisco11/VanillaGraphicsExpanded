using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TexStorageMem3D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexStorageMem3D(TextureTarget @target, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem3D(@target, @levels, @internalFormat, @width, @height, @depth, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexStorageMem3D(TextureTarget @target, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem3D(@target, @levels, @internalFormat, @width, @height, @depth, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexStorageMem3D(TextureTarget @target, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem3D(@target, @levels, @internalFormat, @width, @height, @depth, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexStorageMem3D(TextureTarget @target, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem3D(@target, @levels, @internalFormat, @width, @height, @depth, @memory, @offset);
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
