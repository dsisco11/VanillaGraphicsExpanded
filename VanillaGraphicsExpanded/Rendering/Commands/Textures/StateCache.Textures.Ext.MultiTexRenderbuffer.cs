using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext MultiTexRenderbuffer execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexRenderbuffer(TextureUnit @texunit, TextureTarget @target, int @renderbuffer)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.RenderbufferAssociation, unchecked((int)@renderbuffer));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexRenderbuffer(@texunit, @target, @renderbuffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexRenderbuffer(TextureUnit @texunit, TextureTarget @target, uint @renderbuffer)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.RenderbufferAssociation, unchecked((int)@renderbuffer));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexRenderbuffer(@texunit, @target, @renderbuffer);
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
