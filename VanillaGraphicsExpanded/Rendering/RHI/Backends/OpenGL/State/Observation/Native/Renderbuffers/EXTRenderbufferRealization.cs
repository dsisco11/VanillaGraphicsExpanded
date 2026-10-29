namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes renderbuffer realization by EXT named queries and content commands.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains the creation effect of glGetNamedRenderbufferParameterivEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetNamedRenderbufferParameterivEXT")]
    internal static void ObserveExtRealizationGetNamedRenderbufferParameterivEXT(uint a0, int a1, int* a2)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        GpuResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Renderbuffer, unchecked((int)a0), implicitCreation: true);
    }
    #endregion
}
