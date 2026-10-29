namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes framebuffer realization by EXT named queries and content commands.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains the creation effect of glCheckNamedFramebufferStatusEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glCheckNamedFramebufferStatusEXT")]
    internal static void ObserveExtRealizationCheckNamedFramebufferStatusEXT(uint a0, int a1, int result)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        Current.RealizeFramebuffer(unchecked((int)a0), implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetFramebufferParameterivEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetFramebufferParameterivEXT")]
    internal static void ObserveExtRealizationGetFramebufferParameterivEXT(uint a0, int a1, int* a2)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        Current.RealizeFramebuffer(unchecked((int)a0), implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetNamedFramebufferAttachmentParameterivEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetNamedFramebufferAttachmentParameterivEXT")]
    internal static void ObserveExtRealizationGetNamedFramebufferAttachmentParameterivEXT(uint a0, int a1, int a2, int* a3)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        Current.RealizeFramebuffer(unchecked((int)a0), implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetNamedFramebufferParameterivEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetNamedFramebufferParameterivEXT")]
    internal static void ObserveExtRealizationGetNamedFramebufferParameterivEXT(uint a0, int a1, int* a2)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        Current.RealizeFramebuffer(unchecked((int)a0), implicitCreation: true);
    }
    #endregion
}
