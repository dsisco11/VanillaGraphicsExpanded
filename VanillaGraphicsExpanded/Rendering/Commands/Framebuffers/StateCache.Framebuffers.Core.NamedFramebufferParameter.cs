using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core NamedFramebufferParameter execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreNamedFramebufferParameter(int @framebuffer, FramebufferDefaultParameter @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
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
    internal void ExecuteCoreNamedFramebufferParameter(uint @framebuffer, FramebufferDefaultParameter @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
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
