using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact indexed selectors while propagating established range and generic alias effects.</summary>
internal static unsafe partial class OpenGLBufferBindingCalls
{
    #region Internal API
    /// <summary>Classifies native range rejection before realization or selector withdrawal.</summary>
    internal static bool ValidIndexedRange(BufferRangeTarget target, int buffer, nint offset, nint size, bool multi = false)
    {
        if (offset < 0 || (buffer != 0 || multi) && size <= 0) return false;
        if (buffer == 0 && !multi) return true;
        int alignment = target switch
        {
            BufferRangeTarget.UniformBuffer => GpuSupport.Graphics.UniformBufferOffsetAlignment,
            BufferRangeTarget.ShaderStorageBuffer => GpuSupport.Graphics.ShaderStorageBufferOffsetAlignment,
            BufferRangeTarget.AtomicCounterBuffer or BufferRangeTarget.TransformFeedbackBuffer => 4,
            _ => 0
        };
        return alignment > 0 && offset % alignment == 0
            && (target != BufferRangeTarget.TransformFeedbackBuffer || size % 4 == 0);
    }

    /// <summary>Checks cached slot limits; transform-feedback activity remains a separately observed prerequisite.</summary>
    internal static bool ValidIndexedSlot(BufferRangeTarget target, int slot)
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
