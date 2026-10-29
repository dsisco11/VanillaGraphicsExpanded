using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes the EXT feedback offset selector and its buffer realization effect.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Preserves offset-to-end semantics from maintained capacity without native range discovery.</summary>
    [InterceptOpenGL("glBindBufferOffsetEXT")]
    internal static void ObserveBindBufferOffsetEXT(int target, uint index, uint name, nint offset)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false) || (BufferRangeTarget)target != BufferRangeTarget.TransformFeedbackBuffer
            || index > int.MaxValue || offset < 0 || name != 0 && offset % 4 != 0) return;
        if (cache.TryGetFeedbackActivity(out var establishedActivity) && establishedActivity.Active) return;
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext)
        {
            cache.ObserveFeedbackConfiguration(cache.currentTransformFeedback ?? 0);
            cache.MarkUncertainBufferRealization(unchecked((int)name));
            cache.ForgetBufferBinding((BufferTarget)target, (int)index);
            return;
        }
        if (!OpenGLBufferBindingCalls.ValidIndexedSlot((BufferRangeTarget)target, (int)index)) return;
        if (!cache.TryGetFeedbackActivity(out var activity))
        {
            cache.ObserveFeedbackConfiguration(cache.currentTransformFeedback ?? 0);
            cache.MarkUncertainBufferRealization(unchecked((int)name));
            cache.ForgetBufferBinding((BufferTarget)target, (int)index);
            return;
        }
        if (activity.Active) return;
        int id = unchecked((int)name);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = manager.FindObservedResource(GpuResourceKind.Buffer, id) as GpuResourceMetadataEntry<GpuBufferMetadata>;
        nint size = 0;
        if (id != 0 && entry is { IsUsable: true })
        {
            if (offset >= entry.Metadata.Capacity) return;
            size = entry.Metadata.Capacity - offset;
            size -= size % 4;
            if (size == 0) return;
        }
        cache.ObserveFeedbackConfiguration(cache.currentTransformFeedback ?? 0);
        cache.ForgetBufferBinding((BufferTarget)target, (int)index);
        if (id == 0) cache.EstablishIndexedBufferBinding((BufferRangeTarget)target, (int)index, 0, 0, 0, false);
        else if (size > 0)
            cache.EstablishIndexedBufferBinding((BufferRangeTarget)target, (int)index, id, offset, size, true);
    }
    #endregion
}
