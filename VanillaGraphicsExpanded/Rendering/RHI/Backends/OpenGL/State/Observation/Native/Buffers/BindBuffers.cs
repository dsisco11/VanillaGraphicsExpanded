using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains per-element indexed selectors without multi-bind realization or generic-binding side effects.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Observes existing-object base bindings, preserving rejected elements.</summary>
    [InterceptOpenGL("glBindBuffersBase")]
    internal static void ObserveBindBuffersBase(int target, uint first, int count, uint* names)
    {
        ObserveBufferMultiBind((BufferRangeTarget)target, first, count, names, null, null, false);
    }

    /// <summary>Observes native-width ranges independently for each accepted existing object.</summary>
    [InterceptOpenGL("glBindBuffersRange")]
    internal static void ObserveBindBuffersRange(int target, uint first, int count, uint* names, nint* offsets, nint* sizes)
    {
        ObserveBufferMultiBind((BufferRangeTarget)target, first, count, names, offsets, sizes, true);
    }
    #endregion

    #region Private
    /// <summary>Classifies the entire destination span first and then reconciles element-local outcomes.</summary>
    private static void ObserveBufferMultiBind(BufferRangeTarget target, uint first, int count, uint* names,
        nint* offsets, nint* sizes, bool range)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false) || count < 0 || first > int.MaxValue
            || (ulong)first + (uint)count > int.MaxValue) return;
        if (count == 0 || target is not (BufferRangeTarget.UniformBuffer or BufferRangeTarget.ShaderStorageBuffer
            or BufferRangeTarget.AtomicCounterBuffer or BufferRangeTarget.TransformFeedbackBuffer)) return;
        bool feedback = target == BufferRangeTarget.TransformFeedbackBuffer;
        bool activityKnown = cache.TryGetFeedbackActivity(out var activity);
        if (feedback && activityKnown && activity.Active) return;
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext)
        {
            // Limits are unavailable: expire only existing potentially affected slots,
            // without allocating a span proportional to an untrusted native count.
            foreach (var key in cache.indexedBufferBindings.Keys)
                if (key.Target == target && key.Slot >= first && (ulong)key.Slot < (ulong)first + (uint)count)
                    cache.indexedBufferBindings.Remove(key);
            if (feedback) cache.ObserveFeedbackConfiguration(cache.currentTransformFeedback ?? 0);
            return;
        }
        if (!OpenGLBufferBindingCalls.ValidIndexedSlot(target, (int)first)
            || !OpenGLBufferBindingCalls.ValidIndexedSlot(target, (int)first + count - 1)) return;
        bool changed = false;
        for (int i = 0; i < count; i++)
        {
            int id = names == null ? 0 : unchecked((int)names[i]);
            int slot = (int)first + i;
            if (id != 0 && RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Buffer, id) is GpuResourceMetadataEntry<GpuBufferMetadata> { IsRealized: false, PendingChange: GpuMetadataChange.None }) continue;
            nint offset = 0, size = 0;
            if (range && names != null)
            {
                if (offsets == null || sizes == null) { cache.indexedBufferBindings.Remove((target, slot)); changed = true; continue; }
                offset = offsets[i]; size = sizes[i];
                if (!OpenGLBufferBindingCalls.ValidIndexedRange(target, id, offset, size, multi: true)) continue;
            }
            changed = true;
            if (feedback && !activityKnown || id != 0 && !cache.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, id))
            {
                cache.indexedBufferBindings.Remove((target, slot));
                continue;
            }
            cache.indexedBufferBindings[(target, slot)] = new(id, id == 0 ? 0 : offset, id == 0 ? 0 : size, range && id != 0);
        }
        if (feedback && changed) cache.ObserveFeedbackConfiguration(cache.currentTransformFeedback ?? 0);
    }
    #endregion
}
