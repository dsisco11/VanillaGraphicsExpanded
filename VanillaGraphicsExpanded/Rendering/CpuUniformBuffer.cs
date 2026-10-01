using System;
using System.Numerics;

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
    private Action? writeGuard;

    /// <summary>Attaches an owning shader's edit contract without affecting standalone packed buffers.</summary>
    internal void SetWriteGuard(Action guard) => writeGuard = guard ?? throw new ArgumentNullException(nameof(guard));

    #region Public API
    /// <summary>Allocates zero-initialized packed bytes with no pending changes.</summary>
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
    protected byte[] Data { get { writeGuard?.Invoke(); return data; } }

    /// <summary>
    /// Read-only span view of the buffer data for reading values.
    /// </summary>
    protected ReadOnlySpan<byte> DataReadOnly => data;

    /// <summary>
    /// Writable span view of the buffer data for writing values.
    /// Callers must call <see cref="MarkDirty"/> after modifying the span.
    /// </summary>
    protected Span<byte> DataWritable { get { writeGuard?.Invoke(); return data; } }

    #region Typed parameter writes
    // An unchanged write leaves every previously dirty byte pending.
    // Layout offsets and array strides belong to the derived block. Pack first so a failed
    // write cannot mark the buffer dirty; mark only the bytes actually occupied by the value.
    /// <summary>Writes a float at a byte offset and marks its 4 occupied bytes dirty only on change.</summary>
    protected void WriteFloat(int byteOffset, float value)
    {
        if (UboPacking.WriteFloat(DataWritable, byteOffset, value)) MarkDirty(byteOffset, 4);
    }

    /// <summary>Writes an int at a byte offset and marks its 4 occupied bytes dirty only on change.</summary>
    protected void WriteInt32(int byteOffset, int value)
    {
        if (UboPacking.WriteInt32(DataWritable, byteOffset, value)) MarkDirty(byteOffset, 4);
    }

    /// <summary>Writes a uint at a byte offset and marks its 4 occupied bytes dirty only on change.</summary>
    protected void WriteUInt32(int byteOffset, uint value)
    {
        if (UboPacking.WriteUInt32(DataWritable, byteOffset, value)) MarkDirty(byteOffset, 4);
    }

    /// <summary>Writes a Vector2 at a byte offset and marks its 8 occupied bytes dirty only on change.</summary>
    protected void WriteVector2(int byteOffset, Vector2 value)
    {
        if (UboPacking.WriteVec2(DataWritable, byteOffset, value.X, value.Y)) MarkDirty(byteOffset, 8);
    }

    /// <summary>Writes a Vector3 at a byte offset and marks its 12 occupied bytes dirty only on change.</summary>
    protected void WriteVector3(int byteOffset, Vector3 value)
    {
        if (UboPacking.WriteVec3(DataWritable, byteOffset, value.X, value.Y, value.Z)) MarkDirty(byteOffset, 12);
    }

    /// <summary>Writes a Vector4 at a byte offset and marks its 16 occupied bytes dirty only on change.</summary>
    protected void WriteVector4(int byteOffset, Vector4 value)
    {
        if (UboPacking.WriteVec4(DataWritable, byteOffset, value.X, value.Y, value.Z, value.W)) MarkDirty(byteOffset, 16);
    }

    /// <summary>Writes four int components and marks their std140 slot dirty only on change.</summary>
    protected void WriteIntVector4(int byteOffset, int x, int y, int z, int w)
    {
        if (UboPacking.WriteIVec4(DataWritable, byteOffset, x, y, z, w)) MarkDirty(byteOffset, 16);
    }

    /// <summary>Writes four uint components and marks their std140 slot dirty only on change.</summary>
    protected void WriteUIntVector4(int byteOffset, uint x, uint y, uint z, uint w)
    {
        if (UboPacking.WriteUVec4(DataWritable, byteOffset, x, y, z, w)) MarkDirty(byteOffset, 16);
    }

    /// <summary>Writes sixteen floats in GLSL column order and marks a changed matrix dirty.</summary>
    protected void WriteMatrix4(int byteOffset, ReadOnlySpan<float> columnMajor)
    {
        if (UboPacking.WriteMat4(DataWritable, byteOffset, columnMajor)) MarkDirty(byteOffset, 64);
    }

    /// <summary>Stores Numerics rows as GLSL columns, preserving the equivalent row-vector transform.</summary>
    protected void WriteMatrix4(int byteOffset, in Matrix4x4 value)
    {
        ReadOnlySpan<float> columns = [value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44];
        WriteMatrix4(byteOffset, columns);
    }
    #endregion

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
        writeGuard?.Invoke();
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
        // Preserve the convenience API's no-op behavior when no publication boundary is available.
        _ = TryBindTo(program, blockName, debugName);
    }

    /// <summary>Publishes a complete block and reports whether the active ring bound it successfully.</summary>
    public bool TryBindTo(Shaders.GpuProgram program, string blockName, string debugName)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugName);

        if (uploadScopeDepth != 0)
        {
            // If callers batch updates, they should bind after the scope exits.
            return false;
        }

        if (Bytes.Length == 0)
        {
            return false;
        }

        if (GpuUniformRingSystem.TryBind(program, blockName, Bytes, debugName, clearDirty: true))
        {
            isDirty = false;
            dirtyStartBytes = int.MaxValue;
            dirtyEndExclusiveBytes = 0;
            return true;
        }
        return false;
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
    /// Ends the current batched update scope so a later bind can upload.
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
    ///     // Bind after the scope exits to upload the completed block
    /// }
    /// </code>
    /// </summary>
    public readonly struct UploadScope : IDisposable
    {
        private readonly CpuUniformBuffer buffer;

        /// <summary>Captures the owner of a nested batch scope.</summary>
        internal UploadScope(CpuUniformBuffer buffer)
        {
            this.buffer = buffer;
        }

        /// <summary>Ends this batch scope without altering pending dirty ranges.</summary>
        /// <summary>Releases the CPU-only buffer contract; no GPU resources are owned.</summary>
    public void Dispose()
        {
            buffer?.EndBatchUpdate();
        }
    }

    /// <summary>Releases the CPU-only buffer contract; no GPU resources are owned.</summary>
    public void Dispose()
    {
        // CPU-only: nothing to dispose.
        // Derived classes may override if they add managed/unmanaged resources.
    }
    #endregion
}
