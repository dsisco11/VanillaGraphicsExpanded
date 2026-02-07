using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// CPU-side std140-packed uniform-block data.
///
/// This type is intentionally CPU-only: it owns no GL resources and performs no GL calls.
/// Upload/binding is handled by external systems (e.g., a per-frame UBO ring allocator).
///
/// Usage example (batched updates):
/// <code>
/// using (shader.Params.BeginBatchUpdate())
/// {
///     shader.Intensity = 1.5f;
///     shader.SampleStride = 2;
/// }
/// </code>
/// </summary>
public abstract class CpuUniformBuffer : IDisposable
{
    private readonly byte[] data;
    private bool isDirty;
    private int dirtyStartBytes;
    private int dirtyEndExclusiveBytes;
    private int uploadScopeDepth;

    protected CpuUniformBuffer(int sizeBytes)
    {
        if (sizeBytes <= 0 || sizeBytes > 65536)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "UBO size must be between 1 and 65536 bytes");
        }

        data = new byte[sizeBytes];
        isDirty = false;
        dirtyStartBytes = int.MaxValue;
        dirtyEndExclusiveBytes = 0;
        uploadScopeDepth = 0;
    }

    /// <summary>
    /// Size of the UBO in bytes (std140 layout).
    /// </summary>
    public int SizeBytes => data.Length;

    /// <summary>
    /// Whether the CPU buffer has pending changes that need to be uploaded to GPU.
    /// </summary>
    public bool IsDirty => isDirty;

    /// <summary>
    /// Read-only view of the full packed buffer.
    /// </summary>
    public ReadOnlySpan<byte> Bytes => data;

    /// <summary>
    /// Direct access to the underlying byte array for advanced packing scenarios.
    /// Callers must manually call <see cref="MarkDirty"/> after modifying the buffer.
    /// </summary>
    protected byte[] Data => data;

    /// <summary>
    /// Read-only span view of the buffer data for reading values.
    /// </summary>
    protected ReadOnlySpan<byte> DataReadOnly => data;

    /// <summary>
    /// Writable span view of the buffer data for writing values.
    /// Callers must call <see cref="MarkDirty"/> after modifying the span.
    /// </summary>
    protected Span<byte> DataWritable => data;

    /// <summary>
    /// Marks the buffer as dirty, requiring an upload before the next draw/dispatch.
    /// Called automatically by property setters; manual invocation only needed for direct Data access.
    /// </summary>
    protected void MarkDirty()
    {
        MarkDirty(0, data.Length);
    }

    /// <summary>
    /// Marks a subrange of the buffer as dirty.
    /// This allows uploading only the modified bytes, avoiding overwriting untouched GPU-side values.
    /// </summary>
    /// <param name="byteOffset">Start offset in bytes (0-based).</param>
    /// <param name="byteCount">Number of bytes modified.</param>
    protected void MarkDirty(int byteOffset, int byteCount)
    {
        if (byteOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "Offset must be >= 0.");
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        if (byteCount == 0)
        {
            return;
        }

        int endExclusive = checked(byteOffset + byteCount);
        if (endExclusive > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Dirty range exceeds buffer size.");
        }

        isDirty = true;
        if (byteOffset < dirtyStartBytes) dirtyStartBytes = byteOffset;
        if (endExclusive > dirtyEndExclusiveBytes) dirtyEndExclusiveBytes = endExclusive;
    }

    /// <summary>
    /// Uploads and binds this block using the currently-active UBO ring (if any).
    /// </summary>
    /// <remarks>
    /// This is a convenience entrypoint for existing call sites.
    /// If the ring isn't active on the calling thread, this is a no-op.
    /// </remarks>
    public void BindTo(Shaders.GpuProgram program, string blockName, string debugName)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugName);

        if (uploadScopeDepth != 0)
        {
            // If callers batch updates, they should bind after the scope exits.
            return;
        }

        if (Bytes.Length == 0)
        {
            return;
        }

        if (GpuUniformRingSystem.TryBind(program, blockName, Bytes, debugName, clearDirty: true))
        {
            isDirty = false;
            dirtyStartBytes = int.MaxValue;
            dirtyEndExclusiveBytes = 0;
        }
    }

    /// <summary>
    /// Begins a batched update scope. Property changes within this scope will not trigger uploads
    /// until the scope is disposed or <see cref="EndBatchUpdate"/> is called.
    /// </summary>
    public UploadScope BeginBatchUpdate()
    {
        uploadScopeDepth++;
        return new UploadScope(this);
    }

    /// <summary>
    /// Ends the current batched update scope and uploads if dirty.
    /// </summary>
    private void EndBatchUpdate()
    {
        if (uploadScopeDepth > 0)
        {
            uploadScopeDepth--;
        }
    }

    /// <summary>
    /// RAII scope guard for batching multiple property updates.
    /// Example:
    /// <code>
    /// using (ubo.BeginBatchUpdate())
    /// {
    ///     ubo.Intensity = 1.5f;
    ///     ubo.IndirectTint = new Vec3f(1, 0.9f, 0.8f);
    ///     // Upload happens once when scope exits
    /// }
    /// </code>
    /// </summary>
    public readonly struct UploadScope : IDisposable
    {
        private readonly CpuUniformBuffer buffer;

        internal UploadScope(CpuUniformBuffer buffer)
        {
            this.buffer = buffer;
        }

        public void Dispose()
        {
            buffer?.EndBatchUpdate();
        }
    }

    public void Dispose()
    {
        // CPU-only: nothing to dispose.
        // Derived classes may override if they add managed/unmanaged resources.
    }
}
