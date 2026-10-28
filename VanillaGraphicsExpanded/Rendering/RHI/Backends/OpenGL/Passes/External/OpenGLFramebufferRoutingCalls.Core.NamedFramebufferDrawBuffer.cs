using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core NamedFramebufferDrawBuffer dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLFramebufferRoutingCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferDrawBuffer(int @framebuffer, DrawBufferMode @buf)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.NamedFramebufferDrawBuffer(@framebuffer, @buf);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned, (DrawBufferMode)@buf);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferDrawBuffer(uint @framebuffer, DrawBufferMode @buf)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.NamedFramebufferDrawBuffer(@framebuffer, @buf);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned, (DrawBufferMode)@buf);
        }
    }
    #endregion
}
