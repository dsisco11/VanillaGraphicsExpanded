using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains capture-object configuration revisions without mirroring its indexed layout.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Observes a named whole-buffer output without changing the selected feedback object.</summary>
    [InterceptOpenGL("glTransformFeedbackBufferBase")]
    internal static void ObserveTransformFeedbackBufferBase(uint feedback, uint index, uint buffer)
        => ObserveNamedFeedbackBuffer(unchecked((int)feedback), index, unchecked((int)buffer), 0, 0, false);

    /// <summary>Observes a named aligned output range against cached limits and target-object activity.</summary>
    [InterceptOpenGL("glTransformFeedbackBufferRange")]
    internal static void ObserveTransformFeedbackBufferRange(uint feedback, uint index, uint buffer, nint offset, nint size)
        => ObserveNamedFeedbackBuffer(unchecked((int)feedback), index, unchecked((int)buffer), offset, size, true);

    /// <summary>Expires original capture configuration after an established or uncertain represented update.</summary>
    internal void ObserveFeedbackConfiguration(int feedback)
    {
        InterruptNativeResourceOperation(GpuResourceKind.TransformFeedback, feedback);
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager?.FindObservedResource(GpuResourceKind.TransformFeedback, feedback)
            is GpuResourceMetadataEntry<GpuTransformFeedbackMetadata> { IsUsable: true } entry)
            manager.PublishMetadata(entry, new GpuTransformFeedbackMetadata(), GpuMetadataChange.Metadata);
    }
    #endregion

    #region Private
    /// <summary>Classifies scalar rejection before revision changes, retaining uncertainty without native reads.</summary>
    private static void ObserveNamedFeedbackBuffer(int feedback, uint index, int buffer, nint offset, nint size, bool range)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)
            || !cache.HasEstablishedNativeIdentity(GpuResourceKind.TransformFeedback, feedback)
            || range && (offset < 0 || offset % 4 != 0 || size % 4 != 0 || buffer != 0 && size <= 0)
            || GpuSupport.HasCachedCapabilitiesForCurrentContext
                && index >= GpuSupport.Graphics.MaxTransformFeedbackBuffers
            || cache.feedbackActivity.TryGetValue(feedback, out var activity) && activity.Active) return;
        if (buffer != 0 && RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Buffer, buffer) is GpuResourceMetadataEntry<GpuBufferMetadata> { IsRealized: false, PendingChange: GpuMetadataChange.None }) return;
        cache.ObserveFeedbackConfiguration(feedback);
        if (cache.currentTransformFeedback != feedback || index > int.MaxValue) return;
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext || !cache.feedbackActivity.TryGetValue(feedback, out var known) || known.Active
            || buffer != 0 && !cache.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, buffer))
            cache.indexedBufferBindings.Remove((BufferRangeTarget.TransformFeedbackBuffer, (int)index));
        else cache.indexedBufferBindings[(BufferRangeTarget.TransformFeedbackBuffer, (int)index)]
            = new(buffer, buffer == 0 ? 0 : offset, buffer == 0 ? 0 : size, range && buffer != 0);
    }
    #endregion
}
