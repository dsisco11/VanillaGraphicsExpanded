using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL uniform buffer object (UBO).
/// UBOs are bound to indexed binding points via <c>glBindBufferBase/Range(GL_UNIFORM_BUFFER, ...)</c>.
/// </summary>
public sealed class GpuUniformBuffer : GpuBufferObject
{
    /// <summary>Wraps a newly allocated uniform-buffer object with its usage and debug identity.</summary>
    /// <summary>Declares buffer policy before manager-owned native creation.</summary>
    internal GpuUniformBuffer(BufferUsage usage, string? debugName)
    {
        Role = BufferRole.Uniform;
        allocationUsage = usage;
        this.debugName = debugName;
    }


    /// <summary>
    /// Binds this UBO to a uniform-buffer binding point (base binding).
    /// </summary>
    public void BindBase(int bindingIndex)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuUniformBuffer] Attempted to bind disposed or invalid buffer");
            return;
        }

        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        RHICommandContext.Current.BindBufferBase(BufferRole.Uniform, bindingIndex, this);
    }

    /// <summary>
    /// Binds a subrange of this UBO to a uniform-buffer binding point.
    /// </summary>
    public void BindRange(int bindingIndex, nint offsetBytes, nint sizeBytes)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuUniformBuffer] Attempted to bind disposed or invalid buffer");
            return;
        }

        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        if (offsetBytes < 0 || sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), "Offset must be >= 0 and size must be > 0.");
        }

        RHICommandContext.Current.BindBufferRange(BufferRole.Uniform, bindingIndex, this, offsetBytes, sizeBytes);
    }


    /// <summary>
    /// Unbinds any UBO from a binding point.
    /// </summary>
    public static void UnbindBase(int bindingIndex)
    {
        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        RHICommandContext.Current.BindBufferBase(BufferRole.Uniform, bindingIndex, default);
    }
}
