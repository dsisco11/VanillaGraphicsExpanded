using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glStencilFuncSeparate after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glStencilFuncSeparate")]
    internal static void ObserveStencilFuncSeparate(int a0, int a1, int a2, uint a3)
    {
        StencilFace face = unchecked((StencilFace)a0);
        StencilFunction function = unchecked((StencilFunction)a1);
        int reference = unchecked((int)a2);
        uint mask = unchecked((uint)a3);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            cache = OpenGLObservationContext.CurrentCache;
            bool accepted = face is StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack && Enum.IsDefined(function);
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            // Established rejection preserves prior facts. Observation never vetoes the native call.
            if (accepted) cache.ForgetExternalStateMutation(stencil: NativeStencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction));

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalStencilFuncSeparate(face, function, reference, mask);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.ForgetExternalStateMutation(stencil: NativeStencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction));
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
