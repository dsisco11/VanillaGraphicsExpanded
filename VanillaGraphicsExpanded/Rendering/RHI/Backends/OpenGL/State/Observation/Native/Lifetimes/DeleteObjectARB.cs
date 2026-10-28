using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glDeleteObjectARB after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glDeleteObjectARB")]
    internal static void ObserveDeleteObjectARB(uint a0)
    {
        int id = unchecked((int)a0);
        var cache = OpenGLObservationContext.CurrentCache;
        // Immediate deletion removes native type evidence. Prefer the original registered kind;
        // only an untracked, still-live shader needs native classification for deferred deletion.
        var manager = GpuResourceManagerSystem.CurrentManager;
        bool trackedShader = manager?.FindObservedResource(GpuResourceKind.Shader, id) is not null;
        bool trackedProgram = manager?.FindObservedResource(GpuResourceKind.Program, id) is not null;
        var kind = trackedShader || !trackedProgram && GL.IsShader(id)
            ? GpuResourceKind.Shader : GpuResourceKind.Program;
        cache.InterruptNativeResourceOperation(kind, unchecked((int)id));

        cache.CompleteResourceDeletion(kind, unchecked((int)id), OpenGLRenderingDeletionCalls.WasDeletedExternal(kind, unchecked((int)id)));
    }
    #endregion
}
