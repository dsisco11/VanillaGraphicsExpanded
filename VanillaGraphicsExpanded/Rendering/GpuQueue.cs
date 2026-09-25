using System;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns one bounded SSBO submission from upload through fenced, scoped readback on the current GL context.</summary>
internal sealed class GpuQueue<T> : IDisposable where T : unmanaged
{
    /// <summary>Prevents reuse while completion is unknown, mapping is borrowed or a submission has failed.</summary>
    private enum QueueState { Idle, Pending, Reading, Faulted, Disposed }

    private readonly int capacity, headerBytes, recordBytes = Unsafe.SizeOf<T>();
    private readonly GpuShaderStorageBuffer buffer;
    private GpuFence? fence;
    private QueueState state;
    private bool headerReady;
    private int writtenCount = -1, submittedCount;

    /// <summary>Borrowed for binding and GPU access only; uploads, storage changes and disposal belong to this queue.</summary>
    public GpuShaderStorageBuffer Buffer => buffer;
    public bool Pending => state is QueueState.Pending or QueueState.Reading or QueueState.Faulted;

    #region Storage and submission
    /// <summary>Creates an append queue with a count-prefixed header, or a lazily allocated known-count queue without a header.</summary>
    public GpuQueue(int capacity, int headerBytes = 0, string? debugName = null, BufferUsageHint usage = BufferUsageHint.DynamicDraw)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (headerBytes < 0 || (headerBytes & 3) != 0) throw new ArgumentOutOfRangeException(nameof(headerBytes));
        int maximumBytes = checked(headerBytes + capacity * recordBytes);
        this.capacity = capacity; this.headerBytes = headerBytes;
        buffer = GpuShaderStorageBuffer.Create(usage, debugName);
        try
        {
            // Append producers can write any bounded slot. Known-count producers allocate lazily.
            if (headerBytes > 0) buffer.EnsureCapacity(maximumBytes, growExponentially: false);
        }
        catch { buffer.Dispose(); throw; }
    }

    /// <summary>Uploads a complete domain-owned append header while idle, including its reset count and admission metadata.</summary>
    public void WriteHeader(ReadOnlySpan<uint> header)
    {
        RequireIdle();
        if (headerBytes == 0 || header.Length != (headerBytes >> 2)) throw new ArgumentException("Mismatched GPU queue header.", nameof(header));
        headerReady = false;
        buffer.UploadSubData(header, 0, headerBytes);
        headerReady = true;
    }

    /// <summary>Uploads bounded CPU input for a known-count queue without enlarging its active shader range.</summary>
    public void WriteRecords(ReadOnlySpan<T> records)
    {
        RequireIdle();
        if (headerBytes != 0 || records.Length > capacity) throw new ArgumentException("Invalid known-count GPU queue input.", nameof(records));
        writtenCount = -1;
        if (!records.IsEmpty)
        {
            int bytes = checked(records.Length * recordBytes);
            if (bytes > buffer.SizeBytes)
            {
                // Amortize growing batches without exceeding the queue bound or changing the active range.
                // Widen before doubling so even a near-limit allocation cannot overflow.
                int retainedBytes = (int)Math.Min((long)buffer.SizeBytes << 1, (long)capacity * recordBytes);
                buffer.EnsureCapacity(Math.Max(bytes, retainedBytes), growExponentially: false);
            }
            buffer.UploadSubData(records, 0, bytes);
        }
        writtenCount = records.Length;
    }

    /// <summary>Fences previously issued producer commands; count is supplied only for a headerless queue.</summary>
    public void Submit(int? count = null)
    {
        RequireIdle();
        if (headerBytes > 0 ? count.HasValue || !headerReady : !count.HasValue || count.Value < 0 || count.Value > writtenCount)
            throw new InvalidOperationException("GPU queue submission has no matching prepared input.");
        submittedCount = count.GetValueOrDefault();
        headerReady = false; writtenCount = -1;
        // Commands may already reference this storage even if fence insertion fails. Fail closed until disposal.
        state = QueueState.Pending;
        try { fence = GpuFence.Insert(); GL.Flush(); }
        catch { state = QueueState.Faulted; throw; }
    }
    #endregion

    #region Scoped completion
    /// <summary>Polls without waiting and projects bounded records before releasing their mapping and submission ownership.</summary>
    public bool TryRead<TResult>(GpuQueueReader<T, TResult> reader, out TResult result)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ObjectDisposedException.ThrowIf(state == QueueState.Disposed, this);
        result = default!;
        if (state == QueueState.Idle) return false;
        if (state != QueueState.Pending) throw new InvalidOperationException("GPU queue is faulted or its readback is already borrowed.");
        try
        {
            var status = fence!.Poll();
            if (status == WaitSyncStatus.TimeoutExpired) return false;
            if (status != WaitSyncStatus.AlreadySignaled && status != WaitSyncStatus.ConditionSatisfied)
                throw new InvalidOperationException("GPU queue completion failed.");
            state = QueueState.Reading;
            int count = submittedCount;
            if (headerBytes > 0)
            {
                using var header = buffer.MapRange<uint>(0, 1, MapBufferAccessMask.MapReadBit);
                if (!header.IsMapped) throw new InvalidOperationException("GPU queue count readback failed.");
                count = (int)Math.Min(header.Span[0], (uint)capacity);
            }
            if (count == 0) result = reader(ReadOnlySpan<T>.Empty);
            else
            {
                using var records = buffer.MapRange<T>(headerBytes, count, MapBufferAccessMask.MapReadBit);
                if (!records.IsMapped) throw new InvalidOperationException("GPU queue record readback failed.");
                result = reader(records.Span);
            }
            fence.Dispose(); fence = null;
            state = QueueState.Idle;
            return true;
        }
        catch { state = QueueState.Faulted; throw; }
    }

    /// <summary>Rejects writes and resubmission until the previous borrowed or unknown completion is retired.</summary>
    private void RequireIdle()
    {
        ObjectDisposedException.ThrowIf(state == QueueState.Disposed, this);
        if (state != QueueState.Idle) throw new InvalidOperationException("GPU queue is not idle.");
    }
    #endregion

    #region Lifetime
    /// <summary>Retires pending or failed storage without waiting; a live mapping must finish before disposal.</summary>
    public void Dispose()
    {
        if (state == QueueState.Disposed) return;
        if (state == QueueState.Reading) throw new InvalidOperationException("Cannot dispose a borrowed GPU queue mapping.");
        fence?.Dispose(); fence = null; buffer.Dispose(); state = QueueState.Disposed;
    }
    #endregion
}
