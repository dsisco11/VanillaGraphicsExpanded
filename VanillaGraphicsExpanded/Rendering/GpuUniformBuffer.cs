using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL uniform buffer object (UBO).
/// UBOs are bound to indexed binding points via <c>glBindBufferBase/Range(GL_UNIFORM_BUFFER, ...)</c>.
/// </summary>
public sealed class GpuUniformBuffer : GpuBufferObject
{
    /// <summary>Wraps a newly allocated uniform-buffer object with its usage and debug identity.</summary>
    /// <summary>Declares buffer policy before manager-owned native creation.</summary>
    internal GpuUniformBuffer(RHIBufferUsage usage, string? debugName)
    {
        target = BufferTarget.UniformBuffer;
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

        RHICommandContext.Current.BindBufferBase(RHIBufferRole.Uniform, bindingIndex, this);
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

        RHICommandContext.Current.BindBufferRange(RHIBufferRole.Uniform, bindingIndex, this, offsetBytes, sizeBytes);
    }

    /// <summary>Publishes a logical version with capability validation and truthful failure bookkeeping.</summary>
    internal void BindPublicationRange(int bindingIndex, int offsetBytes, int sizeBytes)
    {
        GpuSupport.EnsureCurrentContext();
        if (!IsValid) throw new ObjectDisposedException(nameof(GpuUniformBuffer));
        if (bindingIndex < 0 || bindingIndex >= GpuSupport.MaxUniformBufferBindings)
            throw new ArgumentOutOfRangeException(nameof(bindingIndex));
        if (sizeBytes <= 0 || sizeBytes > GpuSupport.MaxUniformBlockSize)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        BindRange(bindingIndex, offsetBytes, sizeBytes);
        if (GL.GetError() != ErrorCode.NoError)
        {
            StateCache.Current.InvalidateUniformRange(bindingIndex);
            throw new InvalidOperationException("Uniform range publication failed.");
        }
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

        RHICommandContext.Current.BindBufferBase(RHIBufferRole.Uniform, bindingIndex, default);
    }
}
