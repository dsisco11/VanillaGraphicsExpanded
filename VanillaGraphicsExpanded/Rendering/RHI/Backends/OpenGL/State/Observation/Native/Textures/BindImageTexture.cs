using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindImageTexture after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindImageTexture")]
    internal static void ObserveBindImageTexture(uint a0, uint a1, int a2, byte a3, int a4, int a5, int a6)
    {
        int unit = unchecked((int)a0);
        int texture = unchecked((int)a1);
        int level = unchecked((int)a2);
        bool layered = a3 != 0;
        int layer = unchecked((int)a4);
        TextureAccess access = unchecked((TextureAccess)a5);
        SizedInternalFormat format = unchecked((SizedInternalFormat)a6);
        var cache = OpenGLObservationContext.CurrentCache;
        bool represented = unit >= 0 && unit < GpuSupport.MaxImageUnits;
        bool arguments = represented && level >= 0 && layer >= 0
            && access is TextureAccess.ReadOnly or TextureAccess.WriteOnly or TextureAccess.ReadWrite
            && OpenGLImageBindingCalls.IsImageFormat(format);
        bool established = arguments && cache.HasEstablishedNativeIdentity(GpuResourceKind.Texture, texture);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (arguments) cache.ForgetImageBinding(unit);

        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.EstablishImageBinding(unit, new(texture, level, layered, layer, access, format));
    }
    #endregion
}
