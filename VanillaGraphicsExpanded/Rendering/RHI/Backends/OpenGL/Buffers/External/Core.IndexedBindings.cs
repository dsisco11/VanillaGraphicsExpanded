using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact indexed selectors while propagating established range and generic alias effects.</summary>
internal static unsafe partial class OpenGLBufferBindingCalls
{
    #region Internal API

    #endregion

    #region Private
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
