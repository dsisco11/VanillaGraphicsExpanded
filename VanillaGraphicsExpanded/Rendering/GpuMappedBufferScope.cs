using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII scope for mapping and unmapping a buffer range.
/// </summary>
/// <remarks>
/// This is a thin wrapper over <see cref="GpuBufferObject.MappedRange{T}"/> that provides a stable, non-nested
/// type name for code that wants a dedicated "mapped scope" abstraction.
/// </remarks>
public struct GpuMappedBufferScope<T> : IDisposable where T : unmanaged
{
    private GpuBufferObject.MappedRange<T> range;

    internal GpuMappedBufferScope(GpuBufferObject.MappedRange<T> range)
    {
        this.range = range;
    }

    /// <summary>
    /// Gets whether the underlying buffer mapping is active.
    /// </summary>
    public bool IsMapped => range.IsMapped;

    /// <summary>
    /// Gets the mapped data as a span.
    /// </summary>
    public Span<T> Span => range.Span;

    /// <summary>
    /// Flushes the mapped range if the mapping was created with <see cref="MapBufferAccessMask.MapFlushExplicitBit"/>.
    /// </summary>
    public void Flush() => range.Flush();

    /// <summary>
    /// Flushes a subrange of the mapped range if the mapping was created with <see cref="MapBufferAccessMask.MapFlushExplicitBit"/>.
    /// </summary>
    public void Flush(int relativeOffsetBytes, int byteCount) => range.Flush(relativeOffsetBytes, byteCount);

    /// <summary>
    /// Unmaps the buffer (if mapped).
    /// </summary>
    public void Dispose()
    {
        range.Dispose();
    }
}

