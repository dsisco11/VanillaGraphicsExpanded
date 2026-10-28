using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact indexed selectors while propagating established range and generic alias effects.</summary>
internal static unsafe partial class OpenGLBufferBindingCalls
{
    #region Internal API
    /// <summary>Executes the original whole-buffer overload and publishes its allocation-relative selection.</summary>
    internal static void BindBufferBase(BufferRangeTarget target, int slot, int buffer)
    {
        var cache = EngineStateCalls.CurrentCache;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        bool validSlot = ValidIndexedSlot(target, slot);
        bool established = validSlot && target != BufferRangeTarget.TransformFeedbackBuffer
            && cache.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, buffer);
        if (validSlot) cache.ForgetBufferBinding((BufferTarget)target, slot);
        try { GL.BindBufferBase(target, slot, buffer); }
        catch { if (validSlot && cache.OwnsKnowledgeFor(origin)) cache.ForgetBufferBinding((BufferTarget)target, slot); throw; }
        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.EstablishIndexedBufferBinding(target, slot, buffer, 0, 0, false);
    }

    /// <summary>Preserves pointer-width ranges, defined rejection and the generic selector side effect.</summary>
    internal static void BindBufferRange(BufferRangeTarget target, int slot, int buffer, nint offset, nint size)
    {
        var cache = EngineStateCalls.CurrentCache;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        bool validSlot = ValidIndexedSlot(target, slot);
        int alignment = target switch
        {
            BufferRangeTarget.UniformBuffer => GpuSupport.Graphics.UniformBufferOffsetAlignment,
            BufferRangeTarget.ShaderStorageBuffer => GpuSupport.Graphics.ShaderStorageBufferOffsetAlignment,
            _ => 4
        };
        bool validRange = buffer == 0 || offset >= 0 && size > 0 && alignment > 0 && offset % alignment == 0;
        bool established = validSlot && validRange && target != BufferRangeTarget.TransformFeedbackBuffer
            && cache.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, buffer);
        // These native selectors permit ranges extending beyond the current data store.
        // Managed resource-use validation owns allocation bounds; selector acceptance needs
        // only an established object and the target's alignment/positive-size conditions.
        if (validSlot && validRange) cache.ForgetBufferBinding((BufferTarget)target, slot);
        try { GL.BindBufferRange(target, slot, buffer, offset, size); }
        catch { if (validSlot && cache.OwnsKnowledgeFor(origin)) cache.ForgetBufferBinding((BufferTarget)target, slot); throw; }
        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.EstablishIndexedBufferBinding(target, slot, buffer, buffer == 0 ? 0 : offset, buffer == 0 ? 0 : size, buffer != 0);
    }
    #endregion

    #region Private
    /// <summary>Checks cached slot limits; transform-feedback activity remains a separately observed prerequisite.</summary>
    private static bool ValidIndexedSlot(BufferRangeTarget target, int slot)
    {
        int count = target switch
        {
            BufferRangeTarget.UniformBuffer => GpuSupport.Graphics.MaxUniformBufferBindings,
            BufferRangeTarget.ShaderStorageBuffer => GpuSupport.Graphics.MaxShaderStorageBufferBindings,
            BufferRangeTarget.AtomicCounterBuffer => GpuSupport.Graphics.MaxAtomicCounterBufferBindings,
            BufferRangeTarget.TransformFeedbackBuffer => GpuSupport.Graphics.MaxTransformFeedbackBuffers,
            _ => 0
        };
        return slot >= 0 && slot < count;
    }
    #endregion
}
