using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TextureView execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureView(int @texture, TextureTarget @target, int @origtexture, PixelInternalFormat @internalformat, int @minlevel, int @numlevels, int @minlayer, int @numlayers)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureView(@texture, @target, @origtexture, @internalformat, @minlevel, @numlevels, @minlayer, @numlayers);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureView(uint @texture, TextureTarget @target, uint @origtexture, PixelInternalFormat @internalformat, uint @minlevel, uint @numlevels, uint @minlayer, uint @numlayers)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TextureView(@texture, @target, @origtexture, @internalformat, @minlevel, @numlevels, @minlayer, @numlayers);
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
