using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexStorageMem2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TexStorageMem2D(TextureTarget @target, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem2D(@target, @levels, @internalFormat, @width, @height, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TexStorageMem2D(TextureTarget @target, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem2D(@target, @levels, @internalFormat, @width, @height, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TexStorageMem2D(TextureTarget @target, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem2D(@target, @levels, @internalFormat, @width, @height, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void TexStorageMem2D(TextureTarget @target, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorageMem2D(@target, @levels, @internalFormat, @width, @height, @memory, @offset);
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
