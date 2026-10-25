using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext NamedFramebufferRenderbuffer execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtNamedFramebufferRenderbuffer(int @framebuffer, FramebufferAttachment @attachment, RenderbufferTarget @renderbuffertarget, int @renderbuffer)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), 0, 0, -1, 3);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferRenderbuffer(@framebuffer, @attachment, @renderbuffertarget, @renderbuffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtNamedFramebufferRenderbuffer(uint @framebuffer, FramebufferAttachment @attachment, RenderbufferTarget @renderbuffertarget, uint @renderbuffer)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), 0, 0, -1, 3);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferRenderbuffer(@framebuffer, @attachment, @renderbuffertarget, @renderbuffer);
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
