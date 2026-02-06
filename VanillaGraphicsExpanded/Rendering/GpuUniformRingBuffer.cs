using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Paged ring allocator for uniform-buffer (UBO) updates.
///
/// Intended usage:
/// - Call <see cref="BeginFrame"/> once per frame.
/// - For each draw/dispatch, call <see cref="AllocateAndWrite"/> and bind the returned range via
///   <see cref="GpuUniformBuffer.BindRange"/> (or <see cref="GpuProgramLayout.TryBindUniformBlockRange"/>).
///
/// Preferred path uses persistent mapped storage (GL_ARB_buffer_storage).
/// Fallback path uses orphan+subdata into per-page buffers.
/// </summary>
internal sealed class GpuUniformRingBuffer : IDisposable
{
    public readonly struct Allocation
    {
        public readonly GpuUniformBuffer Buffer;
        public readonly int OffsetBytes;
        public readonly int SizeBytes;

        public Allocation(GpuUniformBuffer buffer, int offsetBytes, int sizeBytes)
        {
            Buffer = buffer;
            OffsetBytes = offsetBytes;
            SizeBytes = sizeBytes;
        }

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

    private int uniformOffsetAlignmentBytes;

    public int PageSizeBytes => pageSizeBytes;
    public int PageCount => pageCount;

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

    public void BeginFrame(int frameIndex)
    {
        if (frameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex), frameIndex, "Frame index must be >= 0.");
        }

        int pageIndex = frameIndex % pageCount;
        WaitForFence(pageIndex);

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
        fences[activePageIndex]?.Dispose();
        fences[activePageIndex] = null;

        fences[activePageIndex] = GpuFence.Insert();
        fences[activePageIndex]!.SetDebugName($"{debugName}.Fence.Page{activePageIndex}");
    }

    public Allocation AllocateAndWrite(ReadOnlySpan<byte> data, int? alignmentBytes = null)
    {
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

        writeOffsetBytes = endExclusive;
        return new Allocation(page, offset, data.Length);
    }

    private void WaitForFence(int pageIndex)
    {
        var fence = fences[pageIndex];
        if (fence is null || !fence.IsValid)
        {
            fences[pageIndex] = null;
            return;
        }

        // Poll with small yields to avoid long blocking; in practice with 3 pages this should be rare.
        for (int i = 0; i < 10_000; i++)
        {
            if (fence.TryConsumeIfSignaled())
            {
                fences[pageIndex] = null;
                return;
            }

            System.Threading.Thread.Yield();
        }

        // Last resort: sleep until signaled.
        while (!fence.TryConsumeIfSignaled())
        {
            System.Threading.Thread.Sleep(0);
        }

        fences[pageIndex] = null;
    }

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

    private static int AlignUp(int value, int alignment)
    {
        if (alignment <= 1)
        {
            return value;
        }

        int mask = alignment - 1;
        return (value + mask) & ~mask;
    }

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

    public void Dispose()
    {
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
}
