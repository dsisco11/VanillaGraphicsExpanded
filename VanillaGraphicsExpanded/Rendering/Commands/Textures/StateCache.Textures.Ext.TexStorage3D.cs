using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TexStorage3D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTexStorage3D(TextureTarget @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.Ext.TexStorage3D(@target, @levels, @internalformat, @width, @height, @depth);
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
