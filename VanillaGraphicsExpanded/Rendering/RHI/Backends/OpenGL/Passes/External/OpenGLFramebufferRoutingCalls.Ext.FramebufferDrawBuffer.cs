using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext FramebufferDrawBuffer dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLFramebufferRoutingCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteExtFramebufferDrawBuffer(int @framebuffer, DrawBufferMode @mode)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferDrawBuffer(@framebuffer, @mode);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteExtFramebufferDrawBuffer(uint @framebuffer, DrawBufferMode @mode)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferDrawBuffer(@framebuffer, @mode);
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
