using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL shader storage buffer object (SSBO).
/// SSBOs are bound to indexed binding points via <c>glBindBufferBase/Range(GL_SHADER_STORAGE_BUFFER, ...)</c>.
/// </summary>
internal sealed class GpuShaderStorageBuffer : GpuBufferObject
{
    private GpuShaderStorageBuffer(int bufferId, BufferUsageHint usage, string? debugName)
    {
        this.bufferId = bufferId;
        target = BufferTarget.ShaderStorageBuffer;
        this.usage = usage;
        this.debugName = debugName;
    }

    /// <summary>
    /// Creates a new SSBO object name and wraps it in an RAII instance.
    /// </summary>
    public static GpuShaderStorageBuffer Create(
        BufferUsageHint usage = BufferUsageHint.DynamicDraw,
        string? debugName = null)
    {
        int id = CreateBufferId(debugName);
        return new GpuShaderStorageBuffer(id, usage, debugName);
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

        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, bindingIndex, bufferId);
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

        GL.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, bindingIndex, bufferId, (IntPtr)offsetBytes, (IntPtr)sizeBytes);
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

        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, bindingIndex, 0);
    }
}

