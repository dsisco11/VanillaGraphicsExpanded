using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext FramebufferReadBuffer dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLFramebufferRoutingCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteExtFramebufferReadBuffer(int @framebuffer, ReadBufferMode @mode)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, true);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferReadBuffer(@framebuffer, @mode);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteExtFramebufferReadBuffer(uint @framebuffer, ReadBufferMode @mode)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, true);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferReadBuffer(@framebuffer, @mode);
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
