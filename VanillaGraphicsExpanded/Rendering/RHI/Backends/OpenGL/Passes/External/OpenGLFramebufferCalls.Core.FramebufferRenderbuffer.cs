using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core FramebufferRenderbuffer execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLFramebufferCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreFramebufferRenderbuffer(FramebufferTarget @target, FramebufferAttachment @attachment, RenderbufferTarget @renderbuffertarget, int @renderbuffer)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), 0, 0, -1, 3);
        bool returned = false;
        try
        {
            GL.FramebufferRenderbuffer(@target, @attachment, @renderbuffertarget, @renderbuffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreFramebufferRenderbuffer(FramebufferTarget @target, FramebufferAttachment @attachment, RenderbufferTarget @renderbuffertarget, uint @renderbuffer)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), 0, 0, -1, 3);
        bool returned = false;
        try
        {
            GL.FramebufferRenderbuffer(@target, @attachment, @renderbuffertarget, @renderbuffer);
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
