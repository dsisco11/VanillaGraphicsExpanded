using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL shader storage buffer object (SSBO).
/// SSBOs are bound to indexed binding points via <c>glBindBufferBase/Range(GL_SHADER_STORAGE_BUFFER, ...)</c>.
/// </summary>
public sealed class GpuShaderStorageBuffer : GpuBufferObject
{
    /// <summary>Wraps a newly allocated storage-buffer object with its usage and debug identity.</summary>
    /// <summary>Declares buffer policy before manager-owned native creation.</summary>
    internal GpuShaderStorageBuffer(BufferUsage usage, string? debugName)
    {
        target = BufferTarget.ShaderStorageBuffer;
        allocationUsage = usage;
        this.debugName = debugName;
    }



    /// <summary>
    /// Binds this SSBO to a shader storage binding point (base binding).
    /// </summary>
    public void BindBase(int bindingIndex)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuShaderStorageBuffer] Attempted to bind disposed or invalid buffer");
            return;
        }

        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        RHICommandContext.Current.BindBufferBase(BufferRole.ShaderStorage, bindingIndex, this);
    }

    /// <summary>
    /// Binds a subrange of this SSBO to a shader storage binding point.
    /// </summary>
    public void BindRange(int bindingIndex, nint offsetBytes, nint sizeBytes)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuShaderStorageBuffer] Attempted to bind disposed or invalid buffer");
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

        RHICommandContext.Current.BindBufferRange(BufferRole.ShaderStorage, bindingIndex, this, offsetBytes, sizeBytes);
    }

    /// <summary>
    /// Unbinds any SSBO from a binding point.
    /// </summary>
    public static void UnbindBase(int bindingIndex)
    {
        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        RHICommandContext.Current.BindBufferBase(BufferRole.ShaderStorage, bindingIndex, default);
    }
}
