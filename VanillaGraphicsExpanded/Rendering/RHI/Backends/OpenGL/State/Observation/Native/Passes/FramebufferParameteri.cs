using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glFramebufferParameteri after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glFramebufferParameteri")]
    internal static void ObserveFramebufferParameteri(int a0, int a1, int a2)
    {
        FramebufferTarget @target = unchecked((FramebufferTarget)a0);
        FramebufferDefaultParameter @pname = unchecked((FramebufferDefaultParameter)a1);
        int @param = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (unchecked((int)@pname) is >= (int)FramebufferDefaultParameter.FramebufferDefaultWidth and <= (int)FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations)
            ObserveFramebufferMutation(unchecked((int)@target), false, parameter: (FramebufferDefaultParameter)unchecked((int)@pname), value: unchecked((int)@param));
    }
    #endregion
}
