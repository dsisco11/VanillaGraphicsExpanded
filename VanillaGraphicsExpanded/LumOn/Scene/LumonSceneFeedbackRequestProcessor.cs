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
    private readonly Dictionary<ulong, uint> virtualToPhysical;
    private readonly Dictionary<uint, ulong> physicalToVirtual;
    private readonly ILumonScenePageTableWriter pageTableWriter;

    public LumonSceneFeedbackRequestProcessor(
        LumonScenePhysicalFieldPool pool,
        LumonScenePageTableEntry[] pageTableMirror,
        Dictionary<ulong, uint> virtualToPhysical,
        Dictionary<uint, ulong> physicalToVirtual,
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
        ReadOnlySpan<ulong> recaptureVirtualPageKeys,
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

        int chunkSlotCount = Math.Max(1, pageTableMirror.Length / LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk);

        for (int i = 0; i < toProcess; i++)
        {
            LumonScenePageRequestGpu req = requests[i];

            uint chunkSlot = req.ChunkSlot;
            if (chunkSlot >= (uint)chunkSlotCount)
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

            ulong key = LumonSceneVirtualPageKeyUtil.Pack(chunkSlot, (uint)vpage);
            int mirrorIndex = checked((int)chunkSlot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk + vpage);

            if (virtualToPhysical.TryGetValue(key, out uint existing))
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
            virtualToPhysical[key] = physicalPageId;
            physicalToVirtual[physicalPageId] = key;

            LumonScenePageTableEntry entry = LumonScenePageTableEntryPacking.Pack(
                physicalPageId: physicalPageId,
                flags: LumonScenePageTableEntryPacking.Flags.Resident
                    | LumonScenePageTableEntryPacking.Flags.NeedsCapture
                    | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

            pageTableMirror[mirrorIndex] = entry;
            pageTableWriter.WriteMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: vpage, entry.Packed);

            if (captureCount < captureWorkOut.Length)
            {
                captureWorkOut[captureCount++] = new LumonSceneCaptureWorkGpu(physicalPageId, chunkSlot: chunkSlot, patchId: patchId, virtualPageIndex: (uint)vpage);
            }

            if (relightCount < relightWorkOut.Length)
            {
                relightWorkOut[relightCount++] = new LumonSceneRelightWorkGpu(physicalPageId, chunkSlot: chunkSlot, patchId: patchId, virtualPageIndex: (uint)vpage);
            }
        }

        int recaptureAttempted = 0;
        int recaptureSucceeded = 0;

        // Dirty-driven recapture: re-capture some resident pages each frame until drained.
        if (!recaptureVirtualPageKeys.IsEmpty)
        {
            for (int i = 0; i < maxRecapture && recaptureCursor < recaptureVirtualPageKeys.Length; i++, recaptureCursor++)
            {
                recaptureAttempted++;
                ulong key = recaptureVirtualPageKeys[recaptureCursor];
                uint chunkSlot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
                int vpage = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
                if ((uint)chunkSlot >= (uint)chunkSlotCount || (uint)vpage >= (uint)LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk)
                {
                    continue;
                }

                int mirrorIndex = checked((int)chunkSlot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk + vpage);
                if ((uint)mirrorIndex >= (uint)pageTableMirror.Length)
                {
                    continue;
                }

                if (!virtualToPhysical.TryGetValue(key, out uint physicalPageId))
                {
                    continue;
                }

                var existingEntry = pageTableMirror[mirrorIndex];
                uint pid = LumonScenePageTableEntryPacking.UnpackPhysicalPageId(existingEntry);
                if (pid == 0)
                {
                    continue;
                }

                var flags = LumonScenePageTableEntryPacking.UnpackFlags(existingEntry);
                flags |= LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.NeedsRelight;
                LumonScenePageTableEntry updated = LumonScenePageTableEntryPacking.Pack(pid, flags);
                pageTableMirror[mirrorIndex] = updated;
                pageTableWriter.WriteMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: vpage, updated.Packed);
                recaptureSucceeded++;

                if (captureCount < captureWorkOut.Length)
                {
                    captureWorkOut[captureCount++] = new LumonSceneCaptureWorkGpu(physicalPageId, chunkSlot: chunkSlot, patchId: (uint)vpage, virtualPageIndex: (uint)vpage);
                }

                if (relightCount < relightWorkOut.Length)
                {
                    relightWorkOut[relightCount++] = new LumonSceneRelightWorkGpu(physicalPageId, chunkSlot: chunkSlot, patchId: (uint)vpage, virtualPageIndex: (uint)vpage);
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
        // Residency policy (v2): do not evict pages while their chunkSlot is still within the active loaded range.
        // Pages are released when a chunkSlot is reassigned (anchor shift) or on world leave/reset.
        didEvict = false;
        return pool.TryAllocate(out page);
    }
}
