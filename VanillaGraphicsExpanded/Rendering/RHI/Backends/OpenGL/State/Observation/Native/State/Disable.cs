using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glDisable after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glDisable")]
    internal static void ObserveDisable(int a0)
    {
        EnableCap cap = unchecked((EnableCap)a0);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            if (!OpenGLObservationContext.TryGet(out cache, out operationContext))
            {
                // Missing limits cannot certify this field; never preserve a potentially stale value.
                if (cache.OwnsKnowledgeFor(operationContext)) AdmitNativeCapability(cache, cap);
                return;
            }
            bool accepted = OpenGlStateCache.AcceptsExternalCapability(cap);
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            if (accepted) AdmitNativeCapability(cache, cap);

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalCapability(cap, false);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    AdmitNativeCapability(cache, cap);
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
