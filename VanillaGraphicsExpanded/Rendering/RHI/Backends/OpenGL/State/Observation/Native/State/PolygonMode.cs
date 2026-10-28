using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glPolygonMode after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glPolygonMode")]
    internal static void ObservePolygonMode(int a0, int a1)
    {
        MaterialFace face = unchecked((MaterialFace)a0);
        PolygonMode mode = unchecked((PolygonMode)a1);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            if (!OpenGLObservationContext.TryGet(out cache, out operationContext))
            {
                // Missing limits cannot certify this field; never preserve a potentially stale value.
                if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);
                return;
            }
            bool accepted = (int)face == (int)MaterialFace.FrontAndBack && Enum.IsDefined(mode);
            // Established rejection preserves prior facts. Observation never vetoes the native call.
            // Separate face modes are outside the supported core model. A compatibility
            // context may still accept them, so they cannot leave a common mode certified.
            if (accepted || !GpuSupport.Graphics.CoreProfile && ((int)face == (int)MaterialFace.Front || (int)face == (int)MaterialFace.Back))
                cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalPolygonMode(face, mode);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
