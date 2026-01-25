using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL uniform buffer object (UBO).
/// UBOs are bound to indexed binding points via <c>glBindBufferBase/Range(GL_UNIFORM_BUFFER, ...)</c>.
/// </summary>
internal sealed class GpuUniformBuffer : GpuBufferObject
{
    private GpuUniformBuffer(int bufferId, BufferUsageHint usage, string? debugName)
    {
        this.bufferId = bufferId;
        target = BufferTarget.UniformBuffer;
        this.usage = usage;
        this.debugName = debugName;
    }

    /// <summary>
    /// Creates a new UBO object name and wraps it in an RAII instance.
    /// </summary>
    public static GpuUniformBuffer Create(
        BufferUsageHint usage = BufferUsageHint.DynamicDraw,
        string? debugName = null)
    {
        int id = CreateBufferId(debugName);
        return new GpuUniformBuffer(id, usage, debugName);
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

        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, bindingIndex, bufferId);
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

        GL.BindBufferRange(BufferRangeTarget.UniformBuffer, bindingIndex, bufferId, (IntPtr)offsetBytes, (IntPtr)sizeBytes);
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

        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, bindingIndex, 0);
    }
}

