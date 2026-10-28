using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindBufferARB after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindBufferARB")]
    internal static void ObserveBindBufferARB(int a0, uint a1)
    {
        BufferTargetArb @target = unchecked((BufferTargetArb)a0);
        int @buffer = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveBufferBinding((BufferTarget)@target, unchecked((int)@buffer));
    }
    #endregion
}
