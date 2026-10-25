using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core FramebufferParameter execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreFramebufferParameter(FramebufferTarget @target, FramebufferDefaultParameter @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@target), false, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.FramebufferParameter(@target, @pname, @param);
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
