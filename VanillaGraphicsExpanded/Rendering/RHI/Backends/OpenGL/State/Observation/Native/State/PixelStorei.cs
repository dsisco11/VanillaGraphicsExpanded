using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glPixelStorei after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glPixelStorei")]
    internal static void ObservePixelStorei(int a0, int a1)
    {
        PixelStoreParameter parameter = unchecked((PixelStoreParameter)a0);
        int value = a1;
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;

                var origin = Integration.RenderContextRegistry.CaptureResourceContext();
                // Callbacks must not observe the previous field as current while its native effect is in progress.
                if (OpenGLPixelStoreCalls.Accepts(parameter, value)) OpenGLPixelStoreCalls.ForgetField(cache, parameter);

                if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
                { if (cache.OwnsKnowledgeFor(origin)) OpenGLPixelStoreCalls.ForgetField(cache, parameter); return; }
                if (cache.OwnsKnowledgeFor(origin) && OpenGLPixelStoreCalls.Accepts(parameter, value)) OpenGLPixelStoreCalls.CommitField(cache, parameter, value);
    }
    #endregion
}
