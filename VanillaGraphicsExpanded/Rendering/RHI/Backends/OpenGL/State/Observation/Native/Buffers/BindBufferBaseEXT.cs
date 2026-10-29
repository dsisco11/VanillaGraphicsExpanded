using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Restricts the EXT feedback-only binding alias to its native target contract.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles the established feedback alias without accepting core-only target namespaces.</summary>
    [InterceptOpenGL("glBindBufferBaseEXT")]
    internal static void ObserveBindBufferBaseEXT(int target, uint index, uint name)
    {
        if ((BufferRangeTarget)target == BufferRangeTarget.TransformFeedbackBuffer)
        {
            if (!OpenGLObservationContext.TryGet(out var selected, out _, false)) return;
            if (GpuResourceManagerSystem.CurrentManager!.FindObservedResource(GpuResourceKind.Buffer, unchecked((int)name))
                is { IsRealized: false })
            {
                if (GpuSupport.HasCachedCapabilitiesForCurrentContext
                    && (!OpenGLBufferBindingCalls.ValidIndexedSlot((BufferRangeTarget)target, unchecked((int)index))
                        || selected.TryGetFeedbackActivity(out var capture) && capture.Active)) return;
                selected.MarkUncertainBufferRealization(unchecked((int)name));
                selected.ForgetBufferBinding((BufferTarget)target, unchecked((int)index));
                return;
            }
            ObserveBindBufferBase(target, index, name);
            // The suffix's old fixed-size definition and later core interactions do not
            // establish its sizing mode portably. Keep the generic selector and original
            // buffer defaults, but never certify a whole-buffer restore mode by analogy.
            var cache = Current;
            if (name != 0 && index <= int.MaxValue && GpuSupport.HasCachedCapabilitiesForCurrentContext
                && OpenGLBufferBindingCalls.ValidIndexedSlot((BufferRangeTarget)target, (int)index)
                && cache.TryGetFeedbackActivity(out var activity) && !activity.Active)
                cache.indexedBufferBindings.Remove(((BufferRangeTarget)target, (int)index));
        }
    }
    #endregion
}
