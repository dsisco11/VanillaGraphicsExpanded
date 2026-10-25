using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TextureStorageMem2D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureStorageMem2D(int @texture, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem2D(@texture, @levels, @internalFormat, @width, @height, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureStorageMem2D(int @texture, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem2D(@texture, @levels, @internalFormat, @width, @height, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureStorageMem2D(uint @texture, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem2D(@texture, @levels, @internalFormat, @width, @height, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureStorageMem2D(uint @texture, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem2D(@texture, @levels, @internalFormat, @width, @height, @memory, @offset);
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
