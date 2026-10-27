using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core NamedFramebufferParameter execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLFramebufferCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreNamedFramebufferParameter(int @framebuffer, FramebufferDefaultParameter @pname, int @param)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.NamedFramebufferParameter(@framebuffer, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreNamedFramebufferParameter(uint @framebuffer, FramebufferDefaultParameter @pname, int @param)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.NamedFramebufferParameter(@framebuffer, @pname, @param);
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
