using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBlendFuncSeparate after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBlendFuncSeparate")]
    internal static void ObserveBlendFuncSeparate(int a0, int a1, int a2, int a3)
    {
        BlendingFactorSrc sourceRgb = unchecked((BlendingFactorSrc)a0);
        BlendingFactorDest destinationRgb = unchecked((BlendingFactorDest)a1);
        BlendingFactorSrc sourceAlpha = unchecked((BlendingFactorSrc)a2);
        BlendingFactorDest destinationAlpha = unchecked((BlendingFactorDest)a3);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            cache = OpenGLObservationContext.CurrentCache;
            bool accepted = OpenGlStateCache.AcceptsExternalBlendFactors(new GlBlendFunc(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha));
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            // Established rejection preserves prior facts. Observation never vetoes the native call.
            if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors);

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalBlendFuncSeparate(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors);
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
