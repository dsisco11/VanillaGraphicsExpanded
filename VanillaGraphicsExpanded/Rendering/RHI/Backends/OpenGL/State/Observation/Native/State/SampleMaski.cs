using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glSampleMaski after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glSampleMaski")]
    internal static void ObserveSampleMaski(uint a0, uint a1)
    {
        int index = unchecked((int)a0);
        int mask = unchecked((int)a1);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            if (!OpenGLObservationContext.TryGet(out cache, out operationContext))
            {
                // Missing limits cannot certify this field; never preserve a potentially stale value.
                if (cache.OwnsKnowledgeFor(operationContext)) cache.WithdrawExternalSampleMask(unchecked((int)index));
                return;
            }
            bool accepted = (ulong)index < (ulong)GpuSupport.Graphics.MaxSampleMaskWords;
            // Established rejection preserves prior facts. Observation never vetoes the native call.
            if (accepted) cache.WithdrawExternalSampleMask(unchecked((int)index));

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalSampleMask(index, mask);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.WithdrawExternalSampleMask(unchecked((int)index));
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
