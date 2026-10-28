using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glMultiTexRenderbufferEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glMultiTexRenderbufferEXT")]
    internal static void ObserveMultiTexRenderbufferEXT(int a0, int a1, uint a2)
    {
        TextureUnit @texunit = unchecked((TextureUnit)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @renderbuffer = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.RenderbufferAssociation, unchecked((int)@renderbuffer));
    }
    #endregion
}
