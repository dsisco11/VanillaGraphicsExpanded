using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext NamedFramebufferTexture1D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtNamedFramebufferTexture1D(int @framebuffer, FramebufferAttachment @attachment, TextureTarget @textarget, int @texture, int @level)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferTexture1D(@framebuffer, @attachment, @textarget, @texture, @level);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtNamedFramebufferTexture1D(uint @framebuffer, FramebufferAttachment @attachment, TextureTarget @textarget, uint @texture, int @level)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferTexture1D(@framebuffer, @attachment, @textarget, @texture, @level);
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
