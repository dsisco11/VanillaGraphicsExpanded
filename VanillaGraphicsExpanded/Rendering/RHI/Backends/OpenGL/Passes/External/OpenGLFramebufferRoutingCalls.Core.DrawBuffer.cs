using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core DrawBuffer dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLFramebufferRoutingCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreDrawBuffer(DrawBufferMode @buf)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.DrawBuffer(@buf);
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
