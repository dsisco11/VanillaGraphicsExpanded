using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TextureStorage3DMultisample execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureStorage3DMultisample(int @texture, int @samples, InternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureStorage3DMultisample(@texture, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureStorage3DMultisample(int @texture, int @samples, SizedInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureStorage3DMultisample(@texture, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureStorage3DMultisample(uint @texture, int @samples, InternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureStorage3DMultisample(@texture, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureStorage3DMultisample(uint @texture, int @samples, SizedInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureStorage3DMultisample(@texture, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);
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
