using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext GenerateMipmap execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtGenerateMipmap(GenerateMipmapTarget @target)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.MipGeneration, 0);
        bool returned = false;
        try
        {
            GL.Ext.GenerateMipmap(@target);
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
