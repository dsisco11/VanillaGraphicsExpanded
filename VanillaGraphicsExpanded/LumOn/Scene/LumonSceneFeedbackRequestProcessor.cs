using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal interface ILumonScenePageTableWriter
{
    void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry);
}

/// <summary>
/// CPU-side request processing for feedback-driven residency.
/// Converts GPU page requests into physical page allocations and capture/relight work items, and updates the CPU mirror of the page table.
/// </summary>
internal sealed class LumonSceneFeedbackRequestProcessor
{
    internal readonly record struct ProcessStats(
        int RequestsConsidered,
        int RequestsAcceptedExisting,
        int RequestsAllocatedNew,
        int RequestsSkippedChunkBudget,
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
    private readonly LumonScenePageTableStatsTracker pageTableStats;
    private readonly Func<LumonSceneCaptureWorkGpu, bool, bool>? captureAdmission;

    #region Request processing
    /// <summary>Connects residency ownership and an optional source-aware capture admission policy.</summary>
    public LumonSceneFeedbackRequestProcessor(
        LumonScenePhysicalFieldPool pool,
        LumonScenePageTableEntry[] pageTableMirror,
        Dictionary<ulong, uint> virtualToPhysical,
        Dictionary<uint, ulong> physicalToVirtual,
        ILumonScenePageTableWriter pageTableWriter,
        LumonScenePageTableStatsTracker pageTableStats,
        Func<LumonSceneCaptureWorkGpu, bool, bool>? captureAdmission = null)
    {
        this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
        this.pageTableMirror = pageTableMirror ?? throw new ArgumentNullException(nameof(pageTableMirror));
        this.virtualToPhysical = virtualToPhysical ?? throw new ArgumentNullException(nameof(virtualToPhysical));
        this.physicalToVirtual = physicalToVirtual ?? throw new ArgumentNullException(nameof(physicalToVirtual));
        this.pageTableWriter = pageTableWriter ?? throw new ArgumentNullException(nameof(pageTableWriter));
        this.pageTableStats = pageTableStats ?? throw new ArgumentNullException(nameof(pageTableStats));
        this.captureAdmission = captureAdmission;
    }

    /// <summary>Processes requests and a finite retry sweep without collecting per-slot allocation statistics.</summary>
    public void Process(
        ReadOnlySpan<LumonScenePageRequestGpu> requests,
        int maxRequestsToProcess,
        int maxNewAllocations,
        int maxResidentPagesPerChunkSlot,
        ReadOnlySpan<ulong> recaptureVirtualPageKeys,
        ref int recaptureCursor,
        int maxRecapture,
        Span<LumonSceneCaptureWorkGpu> captureWorkOut,
        Span<LumonSceneRelightWorkGpu> relightWorkOut,
        out int captureCount,
        out int relightCount,
        out ProcessStats stats)
        => Process(
            requests,
            maxRequestsToProcess,
            maxNewAllocations,
            maxResidentPagesPerChunkSlot,
            recaptureVirtualPageKeys,
            ref recaptureCursor,
            maxRecapture,
            captureWorkOut,
            relightWorkOut,
            out captureCount,
            out relightCount,
            out stats,
            newAllocationsByChunkSlot: Span<ushort>.Empty,
            skippedGlobalBudgetByChunkSlot: Span<ushort>.Empty,
            skippedChunkBudgetByChunkSlot: Span<ushort>.Empty,
            allocationFailuresByChunkSlot: Span<ushort>.Empty);

    /// <summary>Allocates residency independently of source availability and spends retry credit only on admitted captures.</summary>
    public void Process(
        ReadOnlySpan<LumonScenePageRequestGpu> requests,
        int maxRequestsToProcess,
        int maxNewAllocations,
        int maxResidentPagesPerChunkSlot,
        ReadOnlySpan<ulong> recaptureVirtualPageKeys,
        ref int recaptureCursor,
        int maxRecapture,
        Span<LumonSceneCaptureWorkGpu> captureWorkOut,
        Span<LumonSceneRelightWorkGpu> relightWorkOut,
        out int captureCount,
        out int relightCount,
        out ProcessStats stats,
        Span<ushort> newAllocationsByChunkSlot,
        Span<ushort> skippedGlobalBudgetByChunkSlot,
        Span<ushort> skippedChunkBudgetByChunkSlot,
        Span<ushort> allocationFailuresByChunkSlot)
    {
        captureCount = 0;
        relightCount = 0;
        stats = default;

        int toProcess = Math.Min(requests.Length, Math.Max(0, maxRequestsToProcess));
        maxNewAllocations = Math.Max(0, maxNewAllocations);
        maxRecapture = Math.Max(0, maxRecapture);

        int newAllocs = 0;
        int existingAccepted = 0;
        int skippedChunkBudget = 0;
        int skippedChunkSlot = 0;
        int skippedOobVirtualPage = 0;
        int skippedBudget = 0;
        int evictions = 0;
        int allocFailures = 0;

        int chunkSlotCount = Math.Max(1, pageTableMirror.Length / LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk);

        bool hasSlotStats = newAllocationsByChunkSlot.Length >= chunkSlotCount
            && skippedGlobalBudgetByChunkSlot.Length >= chunkSlotCount
            && skippedChunkBudgetByChunkSlot.Length >= chunkSlotCount
            && allocationFailuresByChunkSlot.Length >= chunkSlotCount;

        if (hasSlotStats)
        {
            newAllocationsByChunkSlot.Slice(0, chunkSlotCount).Clear();
            skippedGlobalBudgetByChunkSlot.Slice(0, chunkSlotCount).Clear();
            skippedChunkBudgetByChunkSlot.Slice(0, chunkSlotCount).Clear();
            allocationFailuresByChunkSlot.Slice(0, chunkSlotCount).Clear();
        }

        maxResidentPagesPerChunkSlot = Math.Max(0, maxResidentPagesPerChunkSlot);

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

            if (maxResidentPagesPerChunkSlot > 0 && pageTableStats.GetSlot(chunkSlot).Resident >= maxResidentPagesPerChunkSlot)
            {
                skippedChunkBudget++;
                if (hasSlotStats)
                {
                    IncrementClamped(skippedChunkBudgetByChunkSlot, (int)chunkSlot);
                }
                continue;
            }

            if (newAllocs >= maxNewAllocations)
            {
                skippedBudget++;
                if (hasSlotStats)
                {
                    IncrementClamped(skippedGlobalBudgetByChunkSlot, (int)chunkSlot);
                }
                continue;
            }

            if (!TryAllocateOrEvictOne(maxResidentPagesPerChunkSlot, out LumonScenePhysicalPage page, out bool didEvict))
            {
                allocFailures++;
                if (hasSlotStats)
                {
                    IncrementClamped(allocationFailuresByChunkSlot, (int)chunkSlot);
                }
                continue;
            }

            newAllocs++;
            if (didEvict) evictions++;

            if (hasSlotStats)
            {
                IncrementClamped(newAllocationsByChunkSlot, (int)chunkSlot);
            }

            uint physicalPageId = page.PhysicalPageId;
            virtualToPhysical[key] = physicalPageId;
            physicalToVirtual[physicalPageId] = key;

            LumonScenePageTableEntry entry = LumonScenePageTableEntryPacking.Pack(
                physicalPageId: physicalPageId,
                flags: LumonScenePageTableEntryPacking.Flags.Resident
                    | LumonScenePageTableEntryPacking.Flags.NeedsCapture
                    | LumonScenePageTableEntryPacking.Flags.NeedsRelight);

            LumonScenePageTableEntry oldEntry = pageTableMirror[mirrorIndex];
            pageTableMirror[mirrorIndex] = entry;
            pageTableStats.ApplyEntryChange(chunkSlot, in oldEntry, in entry);
            pageTableWriter.WriteMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: vpage, entry.Packed);

            var captureItem = new LumonSceneCaptureWorkGpu(physicalPageId, chunkSlot, patchId, (uint)vpage);
            if (captureAdmission != null && !captureAdmission(captureItem, false)) continue;
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
            // Deferred sources do not consume GPU page credit. The finite input sweep bounds CPU inspection.
            for (; recaptureSucceeded < maxRecapture && recaptureCursor < recaptureVirtualPageKeys.Length; recaptureCursor++)
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

                var captureItem = new LumonSceneCaptureWorkGpu(physicalPageId, chunkSlot, (uint)vpage, (uint)vpage);
                // Eligibility is checked before withdrawing valid lighting or changing capture flags.
                if (captureAdmission != null && !captureAdmission(captureItem, true)) continue;

                var flags = LumonScenePageTableEntryPacking.UnpackFlags(existingEntry);
                flags |= LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.NeedsRelight;
                LumonScenePageTableEntry updated = LumonScenePageTableEntryPacking.Pack(pid, flags);
                LumonScenePageTableEntry oldEntry = existingEntry;
                pageTableMirror[mirrorIndex] = updated;
                pageTableStats.ApplyEntryChange(chunkSlot, in oldEntry, in updated);
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
            RequestsSkippedChunkBudget: skippedChunkBudget,
            RequestsSkippedChunkSlot: skippedChunkSlot,
            RequestsSkippedOobVirtualPage: skippedOobVirtualPage,
            RequestsSkippedBudget: skippedBudget,
            AllocationEvictions: evictions,
            AllocationFailures: allocFailures,
            RecaptureAttempted: recaptureAttempted,
            RecaptureSucceeded: recaptureSucceeded);
    }

    #endregion

    #region Allocation and counters
    private static void IncrementClamped(Span<ushort> counts, int index)
    {
        if ((uint)index >= (uint)counts.Length)
        {
            return;
        }

        ushort v = counts[index];
        if (v == ushort.MaxValue)
        {
            return;
        }

        counts[index] = (ushort)(v + 1);
    }

    private bool TryAllocateOrEvictOne(int maxResidentPagesPerChunkSlot, out LumonScenePhysicalPage page, out bool didEvict)
    {
        didEvict = false;

        if (pool.TryAllocate(out page))
        {
            return true;
        }

        if (maxResidentPagesPerChunkSlot <= 0)
        {
            page = default;
            return false;
        }

        if (!TryEvictOneOverCap(maxResidentPagesPerChunkSlot))
        {
            page = default;
            return false;
        }

        didEvict = true;
        return pool.TryAllocate(out page);
    }

    private bool TryEvictOneOverCap(int maxResidentPagesPerChunkSlot)
    {
        if (maxResidentPagesPerChunkSlot <= 0)
        {
            return false;
        }

        int attempts = 0;
        while (attempts++ < 128 && pool.TryGetEvictionCandidate(out uint physicalPageId))
        {
            if (!physicalToVirtual.TryGetValue(physicalPageId, out ulong key))
            {
                // Untracked page: do not evict here; try a different candidate.
                pool.PagePool.Touch(physicalPageId);
                continue;
            }

            uint chunkSlot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
            if (pageTableStats.GetSlot(chunkSlot).Resident <= maxResidentPagesPerChunkSlot)
            {
                // Not over cap; keep looking.
                pool.PagePool.Touch(physicalPageId);
                continue;
            }

            physicalToVirtual.Remove(physicalPageId);
            virtualToPhysical.Remove(key);

            int vpage = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
            int mirrorIndex = checked((int)chunkSlot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk + vpage);
            if ((uint)mirrorIndex < (uint)pageTableMirror.Length)
            {
                LumonScenePageTableEntry oldEntry = pageTableMirror[mirrorIndex];
                LumonScenePageTableEntry newEntry = default;
                pageTableMirror[mirrorIndex] = newEntry;
                pageTableStats.ApplyEntryChange(chunkSlot, in oldEntry, in newEntry);
            }

            pageTableWriter.WriteMip0(chunkSlot: (int)chunkSlot, virtualPageIndex: vpage, packedEntry: 0u);
            pool.Free(physicalPageId);
            return true;
        }

        return false;
    }
    #endregion
}
