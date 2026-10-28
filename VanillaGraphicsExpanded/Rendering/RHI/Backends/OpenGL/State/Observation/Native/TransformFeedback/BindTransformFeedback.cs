using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindTransformFeedback after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindTransformFeedback")]
    internal static void ObserveBindTransformFeedback(int a0, uint a1)
    {
        TransformFeedbackTarget target = unchecked((TransformFeedbackTarget)a0);
        int feedback = unchecked((int)a1);
        var cache = OpenGLObservationContext.CurrentCache;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        // A post-call native query describes the new selection, not the previously cached
        // object. Only retained activity can establish whether this bind was rejected.
        bool activityKnown = cache.TryGetFeedbackActivity(out var activity);
        bool rejected = target != TransformFeedbackTarget.TransformFeedback || activityKnown && activity.Active && !activity.Paused;
        bool established = !rejected && activityKnown && cache.HasEstablishedNativeIdentity(GpuResourceKind.TransformFeedback, feedback);
        if (!rejected) cache.TransformFeedbackKnowledge = null;

        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.TransformFeedbackKnowledge = feedback;
    }
    #endregion
}
