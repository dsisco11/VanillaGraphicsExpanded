using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal readonly record struct LumonSceneChunkSlotPageTableStats(
    int Resident,
    int NeedsCapture,
    int Capturing,
    int NeedsRelight,
    int Relighting,
    int ReadyToSample);

/// <summary>
/// Tracks per-chunk-slot page-table backlog counts without requiring per-frame scans of the full page table.
/// </summary>
internal sealed class LumonScenePageTableStatsTracker
{
    private int chunkSlotCount;

    private int[] residentBySlot = Array.Empty<int>();
    private int[] needsCaptureBySlot = Array.Empty<int>();
    private int[] capturingBySlot = Array.Empty<int>();
    private int[] needsRelightBySlot = Array.Empty<int>();
    private int[] relightingBySlot = Array.Empty<int>();
    private int[] readyBySlot = Array.Empty<int>();

    private int totalResident;
    private int totalNeedsCapture;
    private int totalCapturing;
    private int totalNeedsRelight;
    private int totalRelighting;
    private int totalReady;

    public int ChunkSlotCount => chunkSlotCount;

    public LumonSceneChunkSlotPageTableStats Total
        => new(
            Resident: totalResident,
            NeedsCapture: totalNeedsCapture,
            Capturing: totalCapturing,
            NeedsRelight: totalNeedsRelight,
            Relighting: totalRelighting,
            ReadyToSample: totalReady);

    public void Reset(int newChunkSlotCount)
    {
        chunkSlotCount = Math.Max(0, newChunkSlotCount);

        residentBySlot = chunkSlotCount == 0 ? Array.Empty<int>() : new int[chunkSlotCount];
        needsCaptureBySlot = chunkSlotCount == 0 ? Array.Empty<int>() : new int[chunkSlotCount];
        capturingBySlot = chunkSlotCount == 0 ? Array.Empty<int>() : new int[chunkSlotCount];
        needsRelightBySlot = chunkSlotCount == 0 ? Array.Empty<int>() : new int[chunkSlotCount];
        relightingBySlot = chunkSlotCount == 0 ? Array.Empty<int>() : new int[chunkSlotCount];
        readyBySlot = chunkSlotCount == 0 ? Array.Empty<int>() : new int[chunkSlotCount];

        totalResident = 0;
        totalNeedsCapture = 0;
        totalCapturing = 0;
        totalNeedsRelight = 0;
        totalRelighting = 0;
        totalReady = 0;
    }

    public LumonSceneChunkSlotPageTableStats GetSlot(uint chunkSlot)
    {
        int s = (int)chunkSlot;
        if ((uint)s >= (uint)chunkSlotCount)
        {
            return default;
        }

        return new LumonSceneChunkSlotPageTableStats(
            Resident: residentBySlot[s],
            NeedsCapture: needsCaptureBySlot[s],
            Capturing: capturingBySlot[s],
            NeedsRelight: needsRelightBySlot[s],
            Relighting: relightingBySlot[s],
            ReadyToSample: readyBySlot[s]);
    }

    public void RecountFromMirror(LumonScenePageTableEntry[] pageTableMirror)
    {
        if (pageTableMirror is null) throw new ArgumentNullException(nameof(pageTableMirror));

        Array.Clear(residentBySlot, 0, residentBySlot.Length);
        Array.Clear(needsCaptureBySlot, 0, needsCaptureBySlot.Length);
        Array.Clear(capturingBySlot, 0, capturingBySlot.Length);
        Array.Clear(needsRelightBySlot, 0, needsRelightBySlot.Length);
        Array.Clear(relightingBySlot, 0, relightingBySlot.Length);
        Array.Clear(readyBySlot, 0, readyBySlot.Length);

        totalResident = 0;
        totalNeedsCapture = 0;
        totalCapturing = 0;
        totalNeedsRelight = 0;
        totalRelighting = 0;
        totalReady = 0;

        if (chunkSlotCount <= 0)
        {
            return;
        }

        int vpc = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
        int expectedLen = checked(vpc * chunkSlotCount);
        if (pageTableMirror.Length < expectedLen)
        {
            return;
        }

        for (int slot = 0; slot < chunkSlotCount; slot++)
        {
            int baseIndex = checked(slot * vpc);
            int end = baseIndex + vpc;

            int resident = 0;
            int needsCapture = 0;
            int capturing = 0;
            int needsRelight = 0;
            int relighting = 0;
            int ready = 0;

            for (int i = baseIndex; i < end; i++)
            {
                LumonScenePageTableEntry entry = pageTableMirror[i];
                if (LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry) == 0u) continue;

                var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
                if ((flags & LumonScenePageTableEntryPacking.Flags.Resident) != 0) resident++;
                if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsCapture) != 0) needsCapture++;
                if ((flags & LumonScenePageTableEntryPacking.Flags.Capturing) != 0) capturing++;
                if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsRelight) != 0) needsRelight++;
                if ((flags & LumonScenePageTableEntryPacking.Flags.Relighting) != 0) relighting++;
                if (LumonScenePageTableEntryPacking.IsReadyForSampling(entry)) ready++;
            }

            residentBySlot[slot] = resident;
            needsCaptureBySlot[slot] = needsCapture;
            capturingBySlot[slot] = capturing;
            needsRelightBySlot[slot] = needsRelight;
            relightingBySlot[slot] = relighting;
            readyBySlot[slot] = ready;

            totalResident += resident;
            totalNeedsCapture += needsCapture;
            totalCapturing += capturing;
            totalNeedsRelight += needsRelight;
            totalRelighting += relighting;
            totalReady += ready;
        }
    }

    public void ApplyEntryChange(uint chunkSlot, in LumonScenePageTableEntry oldEntry, in LumonScenePageTableEntry newEntry)
    {
        int slot = (int)chunkSlot;
        if ((uint)slot >= (uint)chunkSlotCount)
        {
            return;
        }

        DeltaForEntry(in oldEntry, out int oResident, out int oNeedsCap, out int oCapturing, out int oNeedsRel, out int oRelighting, out int oReady);
        DeltaForEntry(in newEntry, out int nResident, out int nNeedsCap, out int nCapturing, out int nNeedsRel, out int nRelighting, out int nReady);

        int dResident = nResident - oResident;
        int dNeedsCap = nNeedsCap - oNeedsCap;
        int dCapturing = nCapturing - oCapturing;
        int dNeedsRel = nNeedsRel - oNeedsRel;
        int dRelighting = nRelighting - oRelighting;
        int dReady = nReady - oReady;

        residentBySlot[slot] += dResident;
        needsCaptureBySlot[slot] += dNeedsCap;
        capturingBySlot[slot] += dCapturing;
        needsRelightBySlot[slot] += dNeedsRel;
        relightingBySlot[slot] += dRelighting;
        readyBySlot[slot] += dReady;

        totalResident += dResident;
        totalNeedsCapture += dNeedsCap;
        totalCapturing += dCapturing;
        totalNeedsRelight += dNeedsRel;
        totalRelighting += dRelighting;
        totalReady += dReady;
    }

    private static void DeltaForEntry(
        in LumonScenePageTableEntry entry,
        out int resident,
        out int needsCapture,
        out int capturing,
        out int needsRelight,
        out int relighting,
        out int ready)
    {
        if (LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry) == 0u)
        {
            resident = 0;
            needsCapture = 0;
            capturing = 0;
            needsRelight = 0;
            relighting = 0;
            ready = 0;
            return;
        }

        var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);

        resident = (flags & LumonScenePageTableEntryPacking.Flags.Resident) != 0 ? 1 : 0;
        needsCapture = (flags & LumonScenePageTableEntryPacking.Flags.NeedsCapture) != 0 ? 1 : 0;
        capturing = (flags & LumonScenePageTableEntryPacking.Flags.Capturing) != 0 ? 1 : 0;
        needsRelight = (flags & LumonScenePageTableEntryPacking.Flags.NeedsRelight) != 0 ? 1 : 0;
        relighting = (flags & LumonScenePageTableEntryPacking.Flags.Relighting) != 0 ? 1 : 0;
        ready = LumonScenePageTableEntryPacking.IsReadyForSampling(entry) ? 1 : 0;
    }
}
