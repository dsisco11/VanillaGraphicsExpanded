using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core NamedFramebufferReadBuffer dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLFramebufferRoutingCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferReadBuffer(int @framebuffer, ReadBufferMode @src)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, true);
        bool returned = false;
        try
        {
            GL.NamedFramebufferReadBuffer(@framebuffer, @src);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferReadBuffer(uint @framebuffer, ReadBufferMode @src)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, true);
        bool returned = false;
        try
        {
            GL.NamedFramebufferReadBuffer(@framebuffer, @src);
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
