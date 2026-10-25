using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext NamedFramebufferTextureLayer execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtNamedFramebufferTextureLayer(int @framebuffer, FramebufferAttachment @attachment, int @texture, int @level, int @layer)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), unchecked((int)@layer), 1);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferTextureLayer(@framebuffer, @attachment, @texture, @level, @layer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtNamedFramebufferTextureLayer(uint @framebuffer, FramebufferAttachment @attachment, uint @texture, int @level, int @layer)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), unchecked((int)@layer), 1);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferTextureLayer(@framebuffer, @attachment, @texture, @level, @layer);
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
