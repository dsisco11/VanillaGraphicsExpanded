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
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        // A post-call native query describes the new selection, not the previously cached
        // object. Only retained activity can establish whether this bind was rejected.
        bool activityKnown = cache.TryGetFeedbackActivity(out var activity);
        bool rejected = target != TransformFeedbackTarget.TransformFeedback || activityKnown && activity.Active && !activity.Paused;
        if (!rejected && !activityKnown)
        {
            // Unknown incoming activity cannot prove acceptance. Recover only the
            // resulting execution selector, not the object's descriptor or layout.
            cache.TransformFeedbackKnowledge = null;
            int actual = OpenGLFeedbackBindings.GetExternalSelection(cache);
            if (actual != feedback) return;
            activityKnown = true;
        }
        if (!rejected && activityKnown && feedback != 0
            && GpuResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.TransformFeedback, feedback)
                is GpuResourceMetadataEntry<GpuTransformFeedbackMetadata> { IsRealized: false } entry)
        {
            entry.Manager.PublishMetadata(entry, new GpuTransformFeedbackMetadata(), GpuMetadataChange.None);
            cache.RecordFeedbackCreated(feedback);
        }
        bool established = !rejected && activityKnown && cache.HasEstablishedNativeIdentity(GpuResourceKind.TransformFeedback, feedback);

        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.TransformFeedbackKnowledge = feedback;
        else if (!rejected) cache.TransformFeedbackKnowledge = null;
    }
    #endregion
}
