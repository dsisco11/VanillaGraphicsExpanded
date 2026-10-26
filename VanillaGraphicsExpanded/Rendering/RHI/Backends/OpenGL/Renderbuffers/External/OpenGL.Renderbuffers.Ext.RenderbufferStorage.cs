using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext RenderbufferStorage execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLRenderbuffersCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtRenderbufferStorage(RenderbufferTarget @target, RenderbufferStorage @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Ext.RenderbufferStorage(@target, @internalformat, @width, @height);
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
