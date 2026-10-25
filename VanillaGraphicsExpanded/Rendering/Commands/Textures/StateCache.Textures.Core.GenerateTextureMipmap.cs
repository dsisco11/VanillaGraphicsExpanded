using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core GenerateTextureMipmap execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreGenerateTextureMipmap(int @texture)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.MipGeneration, 0);
        bool returned = false;
        try
        {
            GL.GenerateTextureMipmap(@texture);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreGenerateTextureMipmap(uint @texture)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.MipGeneration, 0);
        bool returned = false;
        try
        {
            GL.GenerateTextureMipmap(@texture);
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
