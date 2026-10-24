using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core ReadBuffer dispatch and its completion bookkeeping.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal void ExecuteCoreReadBuffer(ReadBufferMode @src)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, true);
        bool returned = false;
        try
        {
            GL.ReadBuffer(@src);
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
