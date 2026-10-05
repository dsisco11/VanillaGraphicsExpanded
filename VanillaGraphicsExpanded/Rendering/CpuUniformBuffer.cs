using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// CPU-side std140-packed uniform-block data.
///
/// Owns CPU bytes and borrows its last published range from the active UBO ring.
/// GPU allocation and binding remain with the ring and uniform-buffer owners.
///
/// Assignments remain CPU-only; the shader activation boundary publishes the complete block.
/// </summary>
public abstract class CpuUniformBuffer : IDisposable
{
    private readonly byte[] data;
    private bool isDirty;
    private int dirtyStartBytes;
    private int dirtyEndExclusiveBytes;
    private Action? writeGuard;
    private ulong contentRevision;
    private ulong uploadedRevision;
    private GpuUniformRingBuffer? uploadedRing;
    private GpuUniformRingBuffer.Allocation uploadedAllocation;

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
        unchecked { contentRevision++; }
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
        => TryBindTo((IShaderSubmissionTarget)program, blockName, debugName);

    /// <summary>Publishes through the common graphics or compute executable boundary.</summary>
    internal bool TryBindTo(IShaderSubmissionTarget program, string blockName, string debugName)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugName);


        if (Bytes.Length == 0)
        {
            return false;
        }

        if (!GpuUniformRingSystem.TryGetCurrent(out var ring)) return false;
        var allocation = GetUpload(ring);
        if (!program.ProgramLayout.TryBindUniformBlockRange(program.ProgramId, blockName,
            allocation.Buffer, allocation.OffsetBytes, allocation.SizeBytes, warn: null)) return false;
        CommitUpload(ring, allocation);
        return true;
    }

    /// <summary>Uploads through the existing ring to a preparation-validated slot, committing dirty work only on success.</summary>
    internal bool TryBindToSlot(int slot)
    {
        if (!GpuUniformRingSystem.TryGetCurrent(out var ring)) return false;
        var allocation = GetUpload(ring);
        allocation.Buffer.BindRange(slot, allocation.OffsetBytes, allocation.SizeBytes);
        CommitUpload(ring, allocation);
        return true;
    }

    /// <summary>Releases the CPU-only buffer contract; no GPU resources are owned.</summary>
    public void Dispose()
    {
        // The ring owns the allocation; release only this borrowed publication record.
        uploadedRing = null;
        uploadedAllocation = default;
    }
    #endregion

    #region Private
    /// <summary>Reuses only unchanged bytes in the same allocator's still-open allocation epoch.</summary>
    private GpuUniformRingBuffer.Allocation GetUpload(GpuUniformRingBuffer ring)
    {
        // A live GL buffer alone does not prove that its range still belongs to this draw epoch.
        // Changed contents always receive a fresh snapshot, preserving earlier submitted draws.
        if (ReferenceEquals(uploadedRing, ring) && uploadedRevision == contentRevision
            && ring.CanReuse(uploadedAllocation)) return uploadedAllocation;
        return ring.AllocateAndWrite(Bytes);
    }

    /// <summary>Retains the successful publication without consuming dirty work on failed binding.</summary>
    private void CommitUpload(GpuUniformRingBuffer ring, GpuUniformRingBuffer.Allocation allocation)
    {
        uploadedRing = ring;
        uploadedAllocation = allocation;
        uploadedRevision = contentRevision;
        isDirty = false;
        dirtyStartBytes = int.MaxValue;
        dirtyEndExclusiveBytes = 0;
    }
    #endregion
}
