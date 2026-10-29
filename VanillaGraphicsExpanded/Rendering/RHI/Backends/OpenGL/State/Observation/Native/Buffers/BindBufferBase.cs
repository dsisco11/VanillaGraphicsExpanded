using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindBufferBase after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindBufferBase")]
    internal static void ObserveBindBufferBase(int a0, uint a1, uint a2)
    {
        BufferRangeTarget target = unchecked((BufferRangeTarget)a0);
        int slot = unchecked((int)a1);
        int buffer = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        if (target is not (BufferRangeTarget.UniformBuffer or BufferRangeTarget.ShaderStorageBuffer
            or BufferRangeTarget.AtomicCounterBuffer or BufferRangeTarget.TransformFeedbackBuffer) || slot < 0) return;
        if (target == BufferRangeTarget.TransformFeedbackBuffer && cache.TryGetFeedbackActivity(out var establishedActivity) && establishedActivity.Active) return;
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext)
        {
            if (target == BufferRangeTarget.TransformFeedbackBuffer) cache.ObserveFeedbackConfiguration(cache.currentTransformFeedback ?? 0);
            cache.MarkUncertainBufferRealization(buffer);
            cache.ForgetBufferBinding((BufferTarget)target, slot);
            return;
        }
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        bool validSlot = OpenGLBufferBindingCalls.ValidIndexedSlot(target, slot);
        bool captureKnown = cache.TryGetFeedbackActivity(out var capture);
        bool captureRejected = target == BufferRangeTarget.TransformFeedbackBuffer && captureKnown && capture.Active;
        if (validSlot && target == BufferRangeTarget.TransformFeedbackBuffer && !captureKnown)
            cache.MarkUncertainBufferRealization(buffer);
        if (validSlot && !captureRejected && (target != BufferRangeTarget.TransformFeedbackBuffer || captureKnown))
            cache.RealizeNativeBuffer(buffer, GpuSupport.HasCachedCapabilitiesForCurrentContext && !GpuSupport.Graphics.CoreProfile);
        bool established = validSlot && (target != BufferRangeTarget.TransformFeedbackBuffer || captureKnown && !capture.Active)
            && cache.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, buffer);
        if (validSlot && !captureRejected) cache.ForgetBufferBinding((BufferTarget)target, slot);

        if (validSlot && !captureRejected && target == BufferRangeTarget.TransformFeedbackBuffer)
            cache.ObserveFeedbackConfiguration(cache.currentTransformFeedback ?? 0);
        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.EstablishIndexedBufferBinding(target, slot, buffer, 0, 0, false);
    }
    #endregion
}
