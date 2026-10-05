using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Paged ring allocator for uniform-buffer (UBO) updates.
///
/// Intended usage:
/// - Call <see cref="BeginFrame"/> once per frame.
/// - For each new snapshot, call <see cref="AllocateAndWrite"/> and bind the returned range via
///   <see cref="GpuUniformBuffer.BindRange"/> (or <see cref="GpuProgramLayout.TryBindUniformBlockRange"/>).
/// - Unchanged CPU blocks may rebind a range while <see cref="CanReuse"/> confirms its epoch.
///
/// Preferred path uses persistent mapped storage (GL_ARB_buffer_storage).
/// Fallback path uses orphan+subdata into per-page buffers.
/// </summary>
internal sealed class GpuUniformRingBuffer : IDisposable
{
    /// <summary>A borrowed immutable byte range, with allocator provenance for same-epoch reuse.</summary>
    public readonly struct Allocation
    {
        public readonly GpuUniformBuffer Buffer;
        public readonly int OffsetBytes;
        public readonly int SizeBytes;

        internal readonly GpuUniformRingBuffer Owner;
        internal readonly ulong Epoch;

        /// <summary>Records a freshly written range and the allocator epoch that owns it.</summary>
        internal Allocation(GpuUniformRingBuffer owner, ulong epoch, GpuUniformBuffer buffer, int offsetBytes, int sizeBytes)
        {
            Owner = owner;
            Epoch = epoch;
            Buffer = buffer;
            OffsetBytes = offsetBytes;
            SizeBytes = sizeBytes;
        }

        /// <summary>Reports physical buffer validity; epoch reuse additionally requires the allocator's check.</summary>
        public bool IsValid => Buffer is not null && Buffer.IsValid && OffsetBytes >= 0 && SizeBytes > 0;
    }

    private readonly int pageSizeBytes;
    private readonly int pageCount;
    private readonly bool usePersistent;
    private readonly bool coherent;
    private readonly string debugName;

    private readonly GpuUniformBuffer[] pages;
    private readonly IntPtr[] mappedBases;
    private readonly GpuFence?[] fences;

    private int activePageIndex;
    private int writeOffsetBytes;
    private ulong allocationEpoch;
    private bool epochClosed;
    private bool disposed;
    private Uniforms.PersistentUniformStorage? persistentStorage;

    private int uniformOffsetAlignmentBytes;

    #region Public API
    public int PageSizeBytes => pageSizeBytes;
    public int PageCount => pageCount;

    /// <summary>Counts successful copies into ring allocations independently of later resource binding.</summary>
    internal long AllocationsWritten { get; private set; }

    /// <summary>Counts payload bytes copied, excluding alignment padding and resource binding.</summary>
    internal long BytesWritten { get; private set; }
    /// <summary>Counts blocking waits for transient page reuse, independently of copied bytes.</summary>
    internal long FenceWaits { get; private set; }
    public int UniformBufferOffsetAlignmentBytes
    {
        get
        {
            if (uniformOffsetAlignmentBytes <= 0)
            {
                uniformOffsetAlignmentBytes = QueryUniformBufferOffsetAlignment();
            }

            return uniformOffsetAlignmentBytes;
        }
    }

    public bool UsesPersistentMapping => usePersistent;

    /// <summary>Creates retained storage lazily under this frame allocator's context and disposal owner.</summary>
    internal Uniforms.PersistentUniformStorage PersistentStorage
    {
        get
        {
            RequireAlive();
            return persistentStorage ??= new(UniformBufferOffsetAlignmentBytes, usePersistent, coherent);
        }
    }

    /// <summary>Rejects publication after the renderer-owned allocator is disposed.</summary>
    internal void RequireAlive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    /// <summary>Requires an open epoch so later GPU uses cannot escape the frame's completion fence.</summary>
    internal void RequireOpenPublication()
    {
        RequireAlive();
        if (epochClosed) throw new InvalidOperationException("Uniform publication requires an open frame.");
    }

    #region Frame lifecycle
    /// <summary>Creates fixed-capacity pages using supported persistent storage or orphaned buffers.</summary>
    public GpuUniformRingBuffer(
        int pageSizeBytes = 1024 * 1024,
        int pageCount = 3,
        bool preferPersistent = true,
        bool coherent = true,
        string debugName = "GpuUniformRingBuffer")
    {
        if (pageSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSizeBytes), pageSizeBytes, "Page size must be > 0.");
        }

        if (pageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount), pageCount, "Page count must be > 0.");
        }

        this.pageSizeBytes = pageSizeBytes;
        this.pageCount = pageCount;
        this.coherent = coherent;
        this.debugName = debugName;

        GpuSupport.EnsureCurrentContext();
        usePersistent = preferPersistent && GpuSupport.SupportsArbBufferStorage;

        pages = new GpuUniformBuffer[pageCount];
        mappedBases = new IntPtr[pageCount];
        fences = new GpuFence?[pageCount];

        for (int i = 0; i < pageCount; i++)
        {
            pages[i] = GpuUniformBuffer.Create(usage: BufferUsageHint.StreamDraw, debugName: $"{debugName}.Page{i}");
            mappedBases[i] = IntPtr.Zero;
        }

        if (usePersistent)
        {
            AllocateAndMapPersistentPages();
        }
        else
        {
            // Allocate empty stores; we will orphan on BeginFrame.
            for (int i = 0; i < pageCount; i++)
            {
                pages[i].EnsureCapacity(pageSizeBytes, growExponentially: false);
            }
        }

        activePageIndex = 0;
        writeOffsetBytes = 0;
    }

    /// <summary>Starts a fresh allocation epoch after waiting for the selected page's previous work.</summary>
    public void BeginFrame(int frameIndex)
    {
        RequireAlive();
        if (frameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex), frameIndex, "Frame index must be >= 0.");
        }

        int pageIndex = frameIndex % pageCount;
        // A repeated begin must also fence earlier submitted snapshots before resetting their page.
        if (!epochClosed && writeOffsetBytes > 0) EndFrame();
        persistentStorage?.SealFrame();
        WaitForFence(pageIndex);

        // Even a repeated frame index resets the page. Old snapshots cannot be rebound
        // in another frame: its later draws would outlive the fence that retired them.
        unchecked { allocationEpoch++; }
        epochClosed = false;
        activePageIndex = pageIndex;
        writeOffsetBytes = 0;

        if (!usePersistent)
        {
            // Orphan the store to reduce sync hazards.
            pages[activePageIndex].Allocate(pageSizeBytes);
        }
    }

    /// <summary>
    /// Inserts a fence for the current page. Call once after submitting all work that references
    /// allocations from the current page.
    /// </summary>
    public void EndFrame()
    {
        RequireAlive();
        // Close reuse before fencing, including when fence creation fails.
        unchecked { allocationEpoch++; }
        epochClosed = true;
        persistentStorage?.SealFrame();
        fences[activePageIndex]?.Dispose();
        fences[activePageIndex] = null;

        fences[activePageIndex] = GpuFence.Insert();
        fences[activePageIndex]!.SetDebugName($"{debugName}.Fence.Page{activePageIndex}");
    }

    #endregion

    #region Snapshot publication
    /// <summary>Copies a complete immutable snapshot into a fresh aligned range of the active page.</summary>
    public Allocation AllocateAndWrite(ReadOnlySpan<byte> data, int? alignmentBytes = null)
    {
        RequireOpenPublication();
        if (data.Length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "Data must be non-empty.");
        }

        int align = alignmentBytes ?? UniformBufferOffsetAlignmentBytes;
        align = Math.Max(1, align);

        int offset = AlignUp(writeOffsetBytes, align);
        int endExclusive = checked(offset + data.Length);

        if (endExclusive > pageSizeBytes)
        {
            throw new InvalidOperationException(
                $"UBO ring page overflow: requested {data.Length} bytes with alignment {align} at offset {offset}, " +
                $"but page size is {pageSizeBytes}. Consider increasing pageSizeBytes.");
        }

        var page = pages[activePageIndex];

        if (usePersistent)
        {
            var basePtr = mappedBases[activePageIndex];
            if (basePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Persistent mapping was enabled but base pointer is null.");
            }

            unsafe
            {
                fixed (byte* src = data)
                {
                    byte* dst = (byte*)basePtr + offset;
                    global::System.Buffer.MemoryCopy(src, dst, data.Length, data.Length);
                }
            }

            if (!coherent)
            {
                using var scope = page.BindScope();
                GL.FlushMappedBufferRange(BufferTarget.UniformBuffer, (IntPtr)offset, data.Length);
            }
        }
        else
        {
            page.UploadSubData<byte>(data, dstOffsetBytes: offset, byteCount: data.Length);
        }

        // Failed native copies never become publications or successful-copy counter entries.
        if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Transient uniform upload failed.");
        writeOffsetBytes = endExclusive;
        AllocationsWritten++;
        BytesWritten += data.Length;
        return new Allocation(this, allocationEpoch, page, offset, data.Length);
    }

    /// <summary>Checks provenance and current lifetime without querying GPU state.</summary>
    internal bool CanReuse(in Allocation allocation) => !disposed && !epochClosed
        && ReferenceEquals(allocation.Owner, this) && allocation.Epoch == allocationEpoch && allocation.IsValid;
    #endregion

    #region Retirement
    /// <summary>Invalidates borrowed ranges and retires the ring's fences, mappings and pages.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        persistentStorage?.Dispose();
        for (int i = 0; i < fences.Length; i++)
        {
            fences[i]?.Dispose();
            fences[i] = null;
        }

        for (int i = 0; i < pages.Length; i++)
        {
            try { pages[i]?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[VGE] Dispose UBO ring page failed: {ex}"); }
            pages[i] = null!;
            mappedBases[i] = IntPtr.Zero;
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Waits for all previously submitted references before recycling a page.</summary>
    private void WaitForFence(int pageIndex)
    {
        var fence = fences[pageIndex];
        if (fence is null || !fence.IsValid)
        {
            fences[pageIndex] = null;
            return;
        }

        // Flush the command stream when waiting; a zero-timeout poll alone cannot guarantee submission.
        var status = fence.Poll();
        while (status == WaitSyncStatus.TimeoutExpired)
        {
            FenceWaits++;
            status = fence.Wait(TimeSpan.FromMilliseconds(1));
        }
        if (status == WaitSyncStatus.WaitFailed) throw new InvalidOperationException("Uniform page fence failed.");
        fence.Dispose();
        fences[pageIndex] = null;
    }

    /// <summary>Creates persistent mappings with the selected coherent or explicit-flush policy.</summary>
    private void AllocateAndMapPersistentPages()
    {
        for (int i = 0; i < pageCount; i++)
        {
            var page = pages[i];
            if (!page.IsValid)
            {
                continue;
            }

            using var scope = page.BindScope();

            BufferStorageFlags storageFlags = BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit | BufferStorageFlags.DynamicStorageBit;
            MapBufferAccessMask mapFlags = MapBufferAccessMask.MapWriteBit | MapBufferAccessMask.MapPersistentBit;

            if (coherent)
            {
                storageFlags |= BufferStorageFlags.MapCoherentBit;
                mapFlags |= MapBufferAccessMask.MapCoherentBit;
            }
            else
            {
                mapFlags |= MapBufferAccessMask.MapFlushExplicitBit;
            }

            GL.BufferStorage(BufferTarget.UniformBuffer, pageSizeBytes, IntPtr.Zero, storageFlags);

            IntPtr ptr = GL.MapBufferRange(BufferTarget.UniformBuffer, IntPtr.Zero, pageSizeBytes, mapFlags);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("glMapBufferRange returned NULL for persistent UBO ring page.");
            }

            mappedBases[i] = ptr;
        }
    }

    /// <summary>Rounds a byte offset to the allocator's power-of-two alignment.</summary>
    private static int AlignUp(int value, int alignment)
    {
        if (alignment <= 1)
        {
            return value;
        }

        int mask = alignment - 1;
        return (value + mask) & ~mask;
    }

    /// <summary>Reads context alignment once, retaining the existing conservative fallback.</summary>
    private static int QueryUniformBufferOffsetAlignment()
    {
        try
        {
            GL.GetInteger(GetPName.UniformBufferOffsetAlignment, out int align);
            return Math.Max(1, align);
        }
        catch
        {
            return 256;
        }
    }

    #endregion
}
