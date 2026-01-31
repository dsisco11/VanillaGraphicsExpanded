using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal interface ILumonScenePageTableWriter
{
    void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry);
}

/// <summary>
/// CPU-side request processing for Phase 22.6 feedback-driven residency (v1).
/// Converts GPU page requests into physical page allocations and capture/relight work items, and updates the CPU mirror of the page table.
/// </summary>
internal sealed class LumonSceneFeedbackRequestProcessor
{
    internal readonly record struct ProcessStats(
        int RequestsConsidered,
        int RequestsAcceptedExisting,
        int RequestsAllocatedNew,
        int RequestsSkippedChunkSlot,
        int RequestsSkippedOobVirtualPage,
        int RequestsSkippedBudget,
        int AllocationEvictions,
        int AllocationFailures,
        int RecaptureAttempted,
        int RecaptureSucceeded);

    private readonly LumonScenePhysicalFieldPool pool;
    private readonly LumonScenePageTableEntry[] pageTableMirror;
    private readonly Dictionary<int, uint> virtualToPhysical;
    private readonly Dictionary<uint, int> physicalToVirtual;
    private readonly ILumonScenePageTableWriter pageTableWriter;

    public LumonSceneFeedbackRequestProcessor(
        LumonScenePhysicalFieldPool pool,
        LumonScenePageTableEntry[] pageTableMirror,
        Dictionary<int, uint> virtualToPhysical,
        Dictionary<uint, int> physicalToVirtual,
        ILumonScenePageTableWriter pageTableWriter)
    {
        this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
        this.pageTableMirror = pageTableMirror ?? throw new ArgumentNullException(nameof(pageTableMirror));
        this.virtualToPhysical = virtualToPhysical ?? throw new ArgumentNullException(nameof(virtualToPhysical));
        this.physicalToVirtual = physicalToVirtual ?? throw new ArgumentNullException(nameof(physicalToVirtual));
        this.pageTableWriter = pageTableWriter ?? throw new ArgumentNullException(nameof(pageTableWriter));
    }

    public void Process(
        ReadOnlySpan<LumonScenePageRequestGpu> requests,
        int maxRequestsToProcess,
        int maxNewAllocations,
        ReadOnlySpan<int> recaptureVirtualPages,
        ref int recaptureCursor,
        int maxRecapture,
        Span<LumonSceneCaptureWorkGpu> captureWorkOut,
        Span<LumonSceneRelightWorkGpu> relightWorkOut,
        out int captureCount,
        out int relightCount,
        out ProcessStats stats)
    {
        captureCount = 0;
        relightCount = 0;
        stats = default;

        int toProcess = Math.Min(requests.Length, Math.Max(0, maxRequestsToProcess));
        maxNewAllocations = Math.Max(0, maxNewAllocations);
        maxRecapture = Math.Max(0, maxRecapture);

        int newAllocs = 0;
        int existingAccepted = 0;
        int skippedChunkSlot = 0;
        int skippedOobVirtualPage = 0;
        int skippedBudget = 0;
        int evictions = 0;
        int allocFailures = 0;

        for (int i = 0; i < toProcess; i++)
        {
            LumonScenePageRequestGpu req = requests[i];

            // v1: only chunkSlot==0 supported.
            if (req.ChunkSlot != 0u)
            {
                skippedChunkSlot++;
                continue;
            }

            int vpage = (int)req.VirtualPageIndex;
            if ((uint)vpage >= (uint)LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk)
            {
                skippedOobVirtualPage++;
                continue;
            }

            uint patchId = req.Flags; // v1: compute encodes original patchId in Flags slot.

            if (virtualToPhysical.TryGetValue(vpage, out uint existing))
            {
                pool.PagePool.Touch(existing);
                existingAccepted++;
                continue;
            }

            if (newAllocs >= maxNewAllocations)
            {
                skippedBudget++;
                continue;
            }

            if (!TryAllocateOrEvictOne(out LumonScenePhysicalPage page, out bool didEvict))
            {
                allocFailures++;
                continue;
            }

            newAllocs++;
            if (didEvict) evictions++;

            uint physicalPageId = page.PhysicalPageId;
            virtualToPhysical[vpage] = physicalPageId;
            physicalToVirtual[physicalPageId] = vpage;

            LumonScenePageTableEntry entry = LumonScenePageTableEntryPacking.Pack(
                physicalPageId: physicalPageId,
                flags: LumonScenePageTableEntryPacking.Flags.Resident
                    | LumonScenePageTableEntryPacking.Flags.NeedsCapture
                    | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

            pageTableMirror[vpage] = entry;
            pageTableWriter.WriteMip0(chunkSlot: 0, virtualPageIndex: vpage, entry.Packed);

            if (captureCount < captureWorkOut.Length)
            {
                captureWorkOut[captureCount++] = new LumonSceneCaptureWorkGpu(physicalPageId, chunkSlot: 0, patchId: patchId, virtualPageIndex: (uint)vpage);
            }

            if (relightCount < relightWorkOut.Length)
            {
                relightWorkOut[relightCount++] = new LumonSceneRelightWorkGpu(physicalPageId, chunkSlot: 0, patchId: patchId, virtualPageIndex: (uint)vpage);
            }
        }

        int recaptureAttempted = 0;
        int recaptureSucceeded = 0;

        // Dirty-driven recapture: re-capture some resident pages each frame until drained.
        if (!recaptureVirtualPages.IsEmpty)
        {
            for (int i = 0; i < maxRecapture && recaptureCursor < recaptureVirtualPages.Length; i++, recaptureCursor++)
            {
                recaptureAttempted++;
                int vpage = recaptureVirtualPages[recaptureCursor];
                if (!virtualToPhysical.TryGetValue(vpage, out uint physicalPageId))
                {
                    continue;
                }

                var existingEntry = pageTableMirror[vpage];
                uint pid = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(existingEntry);
                if (pid == 0)
                {
                    continue;
                }

                var flags = LumonScenePageTableEntryPacking.UnpackFlags(existingEntry);
                flags |= LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.NeedsRelight;
                LumonScenePageTableEntry updated = LumonScenePageTableEntryPacking.Pack(pid, flags);
                pageTableMirror[vpage] = updated;
                pageTableWriter.WriteMip0(chunkSlot: 0, virtualPageIndex: vpage, updated.Packed);
                recaptureSucceeded++;

                if (captureCount < captureWorkOut.Length)
                {
                    captureWorkOut[captureCount++] = new LumonSceneCaptureWorkGpu(physicalPageId, chunkSlot: 0, patchId: (uint)vpage, virtualPageIndex: (uint)vpage);
                }

                if (relightCount < relightWorkOut.Length)
                {
                    relightWorkOut[relightCount++] = new LumonSceneRelightWorkGpu(physicalPageId, chunkSlot: 0, patchId: (uint)vpage, virtualPageIndex: (uint)vpage);
                }
            }
        }

        stats = new ProcessStats(
            RequestsConsidered: toProcess,
            RequestsAcceptedExisting: existingAccepted,
            RequestsAllocatedNew: newAllocs,
            RequestsSkippedChunkSlot: skippedChunkSlot,
            RequestsSkippedOobVirtualPage: skippedOobVirtualPage,
            RequestsSkippedBudget: skippedBudget,
            AllocationEvictions: evictions,
            AllocationFailures: allocFailures,
            RecaptureAttempted: recaptureAttempted,
            RecaptureSucceeded: recaptureSucceeded);
    }

    private bool TryAllocateOrEvictOne(out LumonScenePhysicalPage page, out bool didEvict)
    {
        didEvict = false;
        if (pool.TryAllocate(out page))
        {
            return true;
        }

        if (!pool.TryGetEvictionCandidate(out uint evictId))
        {
            page = default;
            return false;
        }

        if (physicalToVirtual.TryGetValue(evictId, out int vpage))
        {
            physicalToVirtual.Remove(evictId);
            virtualToPhysical.Remove(vpage);
            pageTableMirror[vpage] = default;
            pageTableWriter.WriteMip0(chunkSlot: 0, virtualPageIndex: vpage, packedEntry: 0u);
        }

        pool.Free(evictId);
        didEvict = true;

        return pool.TryAllocate(out page);
    }
}
