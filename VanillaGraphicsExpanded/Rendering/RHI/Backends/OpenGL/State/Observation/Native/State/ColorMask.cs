using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glColorMask after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glColorMask")]
    internal static void ObserveColorMask(byte a0, byte a1, byte a2, byte a3)
    {
        bool red = a0 != 0;
        bool green = a1 != 0;
        bool blue = a2 != 0;
        bool alpha = a3 != 0;
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            cache = OpenGLObservationContext.CurrentCache;
            bool accepted = true;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            // Established rejection preserves prior facts. Observation never vetoes the native call.
            if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask);

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalColorMask(red, green, blue, alpha);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask);
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
