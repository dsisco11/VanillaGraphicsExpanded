using System;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// CPU-side uniform buffer wrapper that manages std140-packed data, deferred uploads, and batched updates.
/// Provides a base for typed UBO parameter classes with automatic packing and upload management.
/// 
/// Usage example (single property):
/// <code>
/// shader.Intensity = 1.5f; // Uploads immediately
/// </code>
/// 
/// Usage example (batched updates):
/// <code>
/// using (shader.Params.BeginBatchUpdate())
/// {
///     shader.Intensity = 1.5f;
///     shader.IndirectTint = new Vec3f(1, 0.9f, 0.8f);
///     shader.SampleStride = 2;
///     // Single upload happens here when scope exits
/// }
/// </code>
/// </summary>
public abstract class CpuUniformBuffer : IDisposable
{
    private readonly byte[] data;
    private GpuUniformBuffer? gpuBuffer;
    private bool ownsGpuBuffer;
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
        ownsGpuBuffer = true;
        isDirty = false;
        dirtyStartBytes = int.MaxValue;
        dirtyEndExclusiveBytes = 0;
        uploadScopeDepth = 0;
    }

    /// <summary>
    /// Attaches an existing GPU UBO so multiple CPU parameter objects can share a single GPU buffer.
    /// When a shared GPU buffer is attached, this instance will not dispose it.
    /// </summary>
    internal void AttachSharedGpuBuffer(GpuUniformBuffer sharedBuffer)
    {
        ArgumentNullException.ThrowIfNull(sharedBuffer);
        gpuBuffer = sharedBuffer;
        ownsGpuBuffer = false;
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

        // Auto-upload if not inside a batching scope.
        if (uploadScopeDepth == 0)
        {
            UploadIfDirty();
        }
    }

    /// <summary>
    /// Ensures the GPU buffer is created.
    /// </summary>
    private void EnsureGpuBuffer(string debugName)
    {
        if (gpuBuffer is not null)
        {
            return;
        }

        gpuBuffer = GpuUniformBuffer.Create(debugName: debugName);
        ownsGpuBuffer = true;
    }

    /// <summary>
    /// Uploads the CPU buffer to GPU if dirty, then clears the dirty flag.
    /// </summary>
    private void UploadIfDirty()
    {
        if (!isDirty || gpuBuffer is null)
        {
            return;
        }

        int start = dirtyStartBytes;
        int endExclusive = dirtyEndExclusiveBytes;

        // If the dirty range markers somehow aren't set, fall back to full upload.
        if (start == int.MaxValue || endExclusive <= start)
        {
            start = 0;
            endExclusive = data.Length;
        }

        int lengthBytes = endExclusive - start;

        // Ensure the GPU buffer has enough capacity for the full UBO.
        // For owned buffers, resizing also initializes contents with the current CPU buffer.
        // For shared buffers, resizing must NOT overwrite untouched GPU bytes, so we only allocate.
        if (gpuBuffer.SizeBytes < data.Length)
        {
            if (ownsGpuBuffer)
            {
                gpuBuffer.UploadOrResize(data, data.Length, growExponentially: false);
            }
            else
            {
                gpuBuffer.EnsureCapacity(data.Length, growExponentially: false);
                gpuBuffer.UploadSubData<byte>((ReadOnlySpan<byte>)data.AsSpan(start, lengthBytes), start, lengthBytes);
            }
        }
        else
        {
            gpuBuffer.UploadSubData<byte>((ReadOnlySpan<byte>)data.AsSpan(start, lengthBytes), start, lengthBytes);
        }

        isDirty = false;
        dirtyStartBytes = int.MaxValue;
        dirtyEndExclusiveBytes = 0;
    }

    /// <summary>
    /// Ensures the GPU buffer exists and uploads pending changes.
    /// </summary>
    public void Upload(string debugName)
    {
        EnsureGpuBuffer(debugName);
        UploadIfDirty();
    }

    /// <summary>
    /// Gets (or creates) the underlying GPU buffer for sharing scenarios.
    /// Intended for internal use to allow multiple CPU parameter objects to attach to the same GPU UBO.
    /// </summary>
    internal GpuUniformBuffer GetOrCreateGpuBuffer(string debugName)
    {
        EnsureGpuBuffer(debugName);
        return gpuBuffer!;
    }

    /// <summary>
    /// Binds this UBO to the specified program's uniform block.
    /// Automatically uploads if dirty.
    /// </summary>
    public void BindTo(GpuProgram program, string blockName, string debugName)
    {
        Upload(debugName);
        program.TryBindUniformBlock(blockName, gpuBuffer!);
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

        if (uploadScopeDepth == 0)
        {
            UploadIfDirty();
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
        if (ownsGpuBuffer)
        {
            gpuBuffer?.Dispose();
        }
        gpuBuffer = null;
        ownsGpuBuffer = true;
    }
}
