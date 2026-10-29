using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindBuffer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindBuffer")]
    internal static void ObserveBindBuffer(int a0, uint a1)
    {
        BufferTarget @target = unchecked((BufferTarget)a0);
        int @buffer = unchecked((int)a1);
        OpenGlStateCache.ObserveBufferBinding(@target, unchecked((int)@buffer));
    }
    #endregion
}
