using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext GenerateMultiTexMipmap execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtGenerateMultiTexMipmap(TextureUnit @texunit, TextureTarget @target)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.MipGeneration, 0);
        bool returned = false;
        try
        {
            GL.Ext.GenerateMultiTexMipmap(@texunit, @target);
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
