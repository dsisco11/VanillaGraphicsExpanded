using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glLinkProgramARB after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glLinkProgramARB")]
    internal static void ObserveLinkProgramARB(uint a0)
    {
        int @programObj = unchecked((int)a0);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveProgramMetadata(unchecked((int)@programObj), true, false, -1, 1, true, false);
    }
    #endregion
}
