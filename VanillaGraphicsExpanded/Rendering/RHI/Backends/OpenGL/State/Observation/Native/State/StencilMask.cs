using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glStencilMask after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glStencilMask")]
    internal static void ObserveStencilMask(uint a0)
    {
        uint mask = unchecked((uint)a0);
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
            if (accepted) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalStencilMask(mask);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
