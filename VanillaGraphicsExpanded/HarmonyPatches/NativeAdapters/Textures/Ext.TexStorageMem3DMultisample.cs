using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexStorageMem3DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TexStorageMem3DMultisample(TextureTarget @target, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem3DMultisample(@target, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TexStorageMem3DMultisample(TextureTarget @target, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem3DMultisample(@target, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TexStorageMem3DMultisample(TextureTarget @target, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem3DMultisample(@target, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TexStorageMem3DMultisample(TextureTarget @target, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem3DMultisample(@target, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);
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
