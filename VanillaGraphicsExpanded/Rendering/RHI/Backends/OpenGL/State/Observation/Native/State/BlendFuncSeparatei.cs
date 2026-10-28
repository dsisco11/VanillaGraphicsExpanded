using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBlendFuncSeparatei after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBlendFuncSeparatei")]
    internal static void ObserveBlendFuncSeparatei(uint a0, int a1, int a2, int a3, int a4)
    {
        int index = unchecked((int)a0);
        BlendingFactorSrc sourceRgb = unchecked((BlendingFactorSrc)a1);
        BlendingFactorDest destinationRgb = unchecked((BlendingFactorDest)a2);
        BlendingFactorSrc sourceAlpha = unchecked((BlendingFactorSrc)a3);
        BlendingFactorDest destinationAlpha = unchecked((BlendingFactorDest)a4);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            cache = OpenGLObservationContext.CurrentCache;
            bool accepted = index >= 0 && index < cache.MaxDrawBuffers && (OpenGlStateCache.AcceptsExternalBlendFactors(new GlBlendFunc(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha)));
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            // Established rejection preserves prior facts. Observation never vetoes the native call.
            if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors, index: index);

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalBlendFuncSeparate(index, sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors, index: index);
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
