using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glEnablei after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glEnablei")]
    internal static void ObserveEnablei(int a0, uint a1)
    {
        IndexedEnableCap cap = unchecked((IndexedEnableCap)a0);
        int index = unchecked((int)a1);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            cache = OpenGLObservationContext.CurrentCache;
            bool scissor = (int)cap == (int)EnableCap.ScissorTest && index == 0;
            bool accepted = scissor || cap == IndexedEnableCap.Blend && index >= 0 && index < cache.MaxDrawBuffers;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            if (accepted) AdmitNativeCapability(cache, cap, index);

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalCapability(cap, index, true);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    AdmitNativeCapability(cache, cap, index);
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
