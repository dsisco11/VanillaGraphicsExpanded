using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glStencilOpSeparate after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glStencilOpSeparate")]
    internal static void ObserveStencilOpSeparate(int a0, int a1, int a2, int a3)
    {
        StencilFace face = unchecked((StencilFace)a0);
        StencilOp fail = unchecked((StencilOp)a1);
        StencilOp depthFail = unchecked((StencilOp)a2);
        StencilOp pass = unchecked((StencilOp)a3);
        OpenGlStateCache? cache = null;
        (nint Handle, long Generation) operationContext = default;
        try
        {
            cache = OpenGlStateCache.Current;
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            cache = OpenGLObservationContext.CurrentCache;
            bool accepted = face is StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack && Enum.IsDefined(fail) && Enum.IsDefined(depthFail) && Enum.IsDefined(pass);
            operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
            // Established rejection preserves prior facts. Observation never vetoes the native call.
            if (accepted) cache.ForgetExternalStateMutation(stencil: NativeStencilFields(face, StencilStateKnowledge.FrontStencilOperation, StencilStateKnowledge.BackStencilOperation));

            if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
                cache.ObserveExternalStencilOpSeparate(face, fail, depthFail, pass);
        }
        catch (Exception)
        {
            // Native dispatch has completed. Withdraw only uncertain affected knowledge
            // when observation fails, without touching a foreign cache.
            try
            {
                if (cache?.OwnsKnowledgeFor(operationContext) == true)
                    cache.ForgetExternalStateMutation(stencil: NativeStencilFields(face, StencilStateKnowledge.FrontStencilOperation, StencilStateKnowledge.BackStencilOperation));
            }
            catch (Exception) { /* Failure recovery must not veto or retry native dispatch. */ }
        }
    }
    #endregion
}
