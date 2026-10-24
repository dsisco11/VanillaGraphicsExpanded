using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorageMem2DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureStorageMem2DMultisample(int @texture, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, bool @fixedSampleLocations, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem2DMultisample(@texture, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureStorageMem2DMultisample(int @texture, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, bool @fixedSampleLocations, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem2DMultisample(@texture, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureStorageMem2DMultisample(uint @texture, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, bool @fixedSampleLocations, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem2DMultisample(@texture, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TextureStorageMem2DMultisample(uint @texture, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, bool @fixedSampleLocations, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TextureStorageMem2DMultisample(@texture, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);
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
