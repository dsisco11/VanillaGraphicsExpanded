using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindBufferRange after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindBufferRange")]
    internal static void ObserveBindBufferRange(int a0, uint a1, uint a2, nint a3, nint a4)
    {
        BufferRangeTarget target = unchecked((BufferRangeTarget)a0);
        int slot = unchecked((int)a1);
        int buffer = unchecked((int)a2);
        nint offset = unchecked((nint)a3);
        nint size = unchecked((nint)a4);
        var cache = OpenGLObservationContext.CurrentCache;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        bool validSlot = OpenGLBufferBindingCalls.ValidIndexedSlot(target, slot);
        int alignment = target switch
        {
            BufferRangeTarget.UniformBuffer => GpuSupport.Graphics.UniformBufferOffsetAlignment,
            BufferRangeTarget.ShaderStorageBuffer => GpuSupport.Graphics.ShaderStorageBufferOffsetAlignment,
            _ => 4
        };
        bool validRange = buffer == 0 || offset >= 0 && size > 0 && alignment > 0 && offset % alignment == 0;
        bool captureKnown = cache.TryGetFeedbackActivity(out var capture);
        bool captureRejected = target == BufferRangeTarget.TransformFeedbackBuffer && captureKnown && capture.Active;
        bool established = validSlot && validRange && (target != BufferRangeTarget.TransformFeedbackBuffer || captureKnown && !capture.Active)
            && cache.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, buffer);
        // These native selectors permit ranges extending beyond the current data store.
        // Managed resource-use validation owns allocation bounds; selector acceptance needs
        // only an established object and the target's alignment/positive-size conditions.
        if (validSlot && validRange && !captureRejected) cache.ForgetBufferBinding((BufferTarget)target, slot);

        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.EstablishIndexedBufferBinding(target, slot, buffer, buffer == 0 ? 0 : offset, buffer == 0 ? 0 : size, buffer != 0);
    }
    #endregion
}
