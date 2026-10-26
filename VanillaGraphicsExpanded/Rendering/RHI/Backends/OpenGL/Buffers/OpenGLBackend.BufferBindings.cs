using System;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Validates semantic buffer inputs before native selector publication.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <summary>Resolves current manager-certified identity before consulting executed knowledge.</summary>
    public void BindBuffer(RHIBufferRole role, GpuBufferRef buffer)
    {
        RequireContext();
        int name = buffer.IsEmpty ? 0 : checked((int)RequireRange(buffer, 0, 0).ResourceId);
        OpenGLBufferBindings.BindBuffer(cache, OpenGLBufferValues.Target(role), name);
    }

    /// <summary>Validates a complete indexed input, preserving empty-slot clearing semantics.</summary>
    public void BindBufferBase(RHIBufferRole role, int slot, GpuBufferRef buffer)
    {
        RequireContext();
        var target = RequireIndexedSlot(role, slot);
        int name = buffer.IsEmpty ? 0 : checked((int)RequireRange(buffer, 0, 0).ResourceId);
        OpenGLBufferBindings.BindBufferBase(cache, target, slot, name);
    }

    /// <summary>Checks bounds/alignment and cached role limits before selecting a range.</summary>
    public void BindBufferRange(RHIBufferRole role, int slot, GpuBufferRef buffer, nint offset, nint size)
    {
        RequireContext();
        var target = RequireIndexedSlot(role, slot);
        if (buffer.IsEmpty) { OpenGLBufferBindings.BindBufferBase(cache, target, slot, 0); return; }
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var owner = RequireRange(buffer, offset, size);
        int alignment = role switch
        {
            RHIBufferRole.Uniform => Math.Max(1, Capabilities.UniformBufferOffsetAlignment),
            RHIBufferRole.ShaderStorage => Math.Max(1, Capabilities.ShaderStorageBufferOffsetAlignment),
            _ => 4
        };
        if (offset % alignment != 0 || role == RHIBufferRole.Uniform && size > Capabilities.MaxUniformBlockSize)
            throw new ArgumentException("Buffer range violates the cached role alignment or block-size limit.");
        OpenGLBufferBindings.BindBufferRange(cache, target, slot, checked((int)owner.ResourceId), offset, size);
    }
    #endregion

    #region Private
    /// <summary>Resolves indexed namespaces using cached limits, without native queries.</summary>
    private Legacy.BufferRangeTarget RequireIndexedSlot(RHIBufferRole role, int slot)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        var target = role switch
        {
            RHIBufferRole.Uniform => Legacy.BufferRangeTarget.UniformBuffer,
            RHIBufferRole.ShaderStorage => Legacy.BufferRangeTarget.ShaderStorageBuffer,
            RHIBufferRole.AtomicCounter => Legacy.BufferRangeTarget.AtomicCounterBuffer,
            RHIBufferRole.TransformFeedback => Legacy.BufferRangeTarget.TransformFeedbackBuffer,
            _ => throw new ArgumentException("This role has no indexed binding namespace.", nameof(role))
        };
        if (role == RHIBufferRole.Uniform && slot >= Capabilities.MaxUniformBufferBindings
            || role == RHIBufferRole.ShaderStorage && slot >= Capabilities.MaxShaderStorageBufferBindings
            || role == RHIBufferRole.AtomicCounter && slot >= Capabilities.MaxAtomicCounterBufferBindings
            || role == RHIBufferRole.TransformFeedback && slot >= Capabilities.MaxTransformFeedbackBuffers)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return target;
    }
    #endregion
}
