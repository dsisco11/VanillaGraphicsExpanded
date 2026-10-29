namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes texture realization by EXT named queries and content commands.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains the creation effect of glCompressedTextureSubImage1DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glCompressedTextureSubImage1DEXT")]
    internal static void ObserveExtRealizationCompressedTextureSubImage1DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, nint a7)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glCompressedTextureSubImage2DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glCompressedTextureSubImage2DEXT")]
    internal static void ObserveExtRealizationCompressedTextureSubImage2DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, nint a9)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glCompressedTextureSubImage3DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glCompressedTextureSubImage3DEXT")]
    internal static void ObserveExtRealizationCompressedTextureSubImage3DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, nint a11)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glCopyTextureSubImage1DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glCopyTextureSubImage1DEXT")]
    internal static void ObserveExtRealizationCopyTextureSubImage1DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glCopyTextureSubImage2DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glCopyTextureSubImage2DEXT")]
    internal static void ObserveExtRealizationCopyTextureSubImage2DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glCopyTextureSubImage3DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glCopyTextureSubImage3DEXT")]
    internal static void ObserveExtRealizationCopyTextureSubImage3DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetCompressedTextureImageEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetCompressedTextureImageEXT")]
    internal static void ObserveExtRealizationGetCompressedTextureImageEXT(uint a0, int a1, int a2, nint a3)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetTextureImageEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetTextureImageEXT")]
    internal static void ObserveExtRealizationGetTextureImageEXT(uint a0, int a1, int a2, int a3, int a4, nint a5)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetTextureLevelParameterfvEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetTextureLevelParameterfvEXT")]
    internal static void ObserveExtRealizationGetTextureLevelParameterfvEXT(uint a0, int a1, int a2, int a3, float* a4)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetTextureLevelParameterivEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetTextureLevelParameterivEXT")]
    internal static void ObserveExtRealizationGetTextureLevelParameterivEXT(uint a0, int a1, int a2, int a3, int* a4)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetTextureParameterIivEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetTextureParameterIivEXT")]
    internal static void ObserveExtRealizationGetTextureParameterIivEXT(uint a0, int a1, int a2, int* a3)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetTextureParameterIuivEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetTextureParameterIuivEXT")]
    internal static void ObserveExtRealizationGetTextureParameterIuivEXT(uint a0, int a1, int a2, uint* a3)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetTextureParameterfvEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetTextureParameterfvEXT")]
    internal static void ObserveExtRealizationGetTextureParameterfvEXT(uint a0, int a1, int a2, float* a3)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glGetTextureParameterivEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glGetTextureParameterivEXT")]
    internal static void ObserveExtRealizationGetTextureParameterivEXT(uint a0, int a1, int a2, int* a3)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glTextureSubImage1DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glTextureSubImage1DEXT")]
    internal static void ObserveExtRealizationTextureSubImage1DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, nint a7)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glTextureSubImage2DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glTextureSubImage2DEXT")]
    internal static void ObserveExtRealizationTextureSubImage2DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, nint a9)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }

    /// <summary>Retains the creation effect of glTextureSubImage3DEXT without reading or changing its output.</summary>
    [InterceptOpenGL("glTextureSubImage3DEXT")]
    internal static void ObserveExtRealizationTextureSubImage3DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, nint a11)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageSelectorTarget(a1, out var target)) return;
        RHIResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, unchecked((int)a0), target, implicitCreation: true);
    }
    #endregion
}
