using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext CopyMultiTexImage1D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtCopyMultiTexImage1D(TextureUnit @texunit, TextureTarget @target, int @level, InternalFormat @internalformat, int @x, int @y, int @width, int @border)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Ext.CopyMultiTexImage1D(@texunit, @target, @level, @internalformat, @x, @y, @width, @border);
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
