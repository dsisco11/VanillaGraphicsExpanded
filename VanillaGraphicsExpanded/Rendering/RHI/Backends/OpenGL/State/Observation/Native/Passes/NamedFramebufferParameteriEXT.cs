using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedFramebufferParameteriEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedFramebufferParameteriEXT")]
    internal static void ObserveNamedFramebufferParameteriEXT(uint a0, int a1, int a2)
    {
        int @framebuffer = unchecked((int)a0);
        ExtDirectStateAccess @pname = unchecked((ExtDirectStateAccess)a1);
        int @param = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        if (unchecked((int)@pname) is >= (int)FramebufferDefaultParameter.FramebufferDefaultWidth and <= (int)FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations)
            ObserveFramebufferMutation(unchecked((int)@framebuffer), true, parameter: (FramebufferDefaultParameter)unchecked((int)@pname));
    }
    #endregion
}
