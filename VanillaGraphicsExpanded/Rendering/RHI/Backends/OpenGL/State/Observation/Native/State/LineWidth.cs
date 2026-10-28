using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glLineWidth after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glLineWidth")]
    internal static void ObserveLineWidth(float a0)
    {
        float width = unchecked((float)a0);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            cache = OpenGLObservationContext.CurrentCache;
            bool accepted = float.IsFinite(width) && width > 0;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            // Established rejection preserves prior facts. Observation never vetoes the native call.
            if (accepted || !float.IsFinite(width)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.LineWidth);

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalLineWidth(width);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.LineWidth);
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
