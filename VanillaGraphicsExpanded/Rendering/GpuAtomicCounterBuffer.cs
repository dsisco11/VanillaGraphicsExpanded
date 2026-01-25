using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL atomic counter buffer (ACBO).
/// Atomic counter buffers are bound to indexed binding points via <c>glBindBufferBase/Range(GL_ATOMIC_COUNTER_BUFFER, ...)</c>.
/// </summary>
internal sealed class GpuAtomicCounterBuffer : GpuBufferObject
{
    private GpuAtomicCounterBuffer(int bufferId, BufferUsageHint usage, string? debugName)
    {
        this.bufferId = bufferId;
        target = BufferTarget.AtomicCounterBuffer;
        this.usage = usage;
        this.debugName = debugName;
    }

    /// <summary>
    /// Creates a new atomic counter buffer object name and wraps it in an RAII instance.
    /// </summary>
    public static GpuAtomicCounterBuffer Create(
        BufferUsageHint usage = BufferUsageHint.DynamicDraw,
        string? debugName = null)
    {
        int id = CreateBufferId(debugName);
        return new GpuAtomicCounterBuffer(id, usage, debugName);
    }

    /// <summary>
    /// Allocates and initializes the buffer with <paramref name="counterCount"/> 32-bit counters.
    /// </summary>
    public void InitializeCounters(int counterCount, uint initialValue = 0)
    {
        if (!IsValid)
        {
            return;
        }

        if (counterCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(counterCount), counterCount, "Counter count must be >= 0.");
        }

        if (counterCount == 0)
        {
            UploadData(ReadOnlySpan<uint>.Empty);
            return;
        }

        var counters = new uint[counterCount];
        if (initialValue != 0)
        {
            Array.Fill(counters, initialValue);
        }

        UploadData(counters);
    }

    /// <summary>
    /// Binds this buffer to an atomic counter binding point (base binding).
    /// </summary>
    public void BindBase(int bindingIndex)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuAtomicCounterBuffer] Attempted to bind disposed or invalid buffer");
            return;
        }

        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        GL.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, bufferId);
    }

    /// <summary>
    /// Binds a subrange of this buffer to an atomic counter binding point.
    /// </summary>
    public void BindRange(int bindingIndex, nint offsetBytes, nint sizeBytes)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuAtomicCounterBuffer] Attempted to bind disposed or invalid buffer");
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

        GL.BindBufferRange(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, bufferId, (IntPtr)offsetBytes, (IntPtr)sizeBytes);
    }

    /// <summary>
    /// Unbinds any atomic counter buffer from a binding point.
    /// </summary>
    public static void UnbindBase(int bindingIndex)
    {
        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        GL.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, 0);
    }
}

