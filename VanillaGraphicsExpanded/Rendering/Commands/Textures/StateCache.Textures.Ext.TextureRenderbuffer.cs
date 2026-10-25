using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TextureRenderbuffer execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureRenderbuffer(int @texture, TextureTarget @target, int @renderbuffer)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.RenderbufferAssociation, unchecked((int)@renderbuffer));
        bool returned = false;
        try
        {
            GL.Ext.TextureRenderbuffer(@texture, @target, @renderbuffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureRenderbuffer(uint @texture, TextureTarget @target, uint @renderbuffer)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.RenderbufferAssociation, unchecked((int)@renderbuffer));
        bool returned = false;
        try
        {
            GL.Ext.TextureRenderbuffer(@texture, @target, @renderbuffer);
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
