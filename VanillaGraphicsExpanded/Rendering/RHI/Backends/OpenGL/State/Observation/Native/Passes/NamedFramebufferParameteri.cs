using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedFramebufferParameteri after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedFramebufferParameteri")]
    internal static void ObserveNamedFramebufferParameteri(uint a0, int a1, int a2)
    {
        int @framebuffer = unchecked((int)a0);
        FramebufferDefaultParameter @pname = unchecked((FramebufferDefaultParameter)a1);
        int @param = unchecked((int)a2);
        _ = OpenGLObservationContext.CurrentCache;
        if (unchecked((int)@pname) is >= (int)FramebufferDefaultParameter.FramebufferDefaultWidth and <= (int)FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations)
            ObserveFramebufferMutation(unchecked((int)@framebuffer), true, parameter: (FramebufferDefaultParameter)unchecked((int)@pname));
    }
    #endregion
}
