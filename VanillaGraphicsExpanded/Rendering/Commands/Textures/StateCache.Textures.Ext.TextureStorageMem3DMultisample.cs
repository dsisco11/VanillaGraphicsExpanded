using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TextureStorageMem3DMultisample execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureStorageMem3DMultisample(int @texture, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem3DMultisample(@texture, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureStorageMem3DMultisample(int @texture, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem3DMultisample(@texture, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureStorageMem3DMultisample(uint @texture, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem3DMultisample(@texture, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureStorageMem3DMultisample(uint @texture, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem3DMultisample(@texture, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);
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
