using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.LumOn.Scene;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneFeedbackRequestProcessingTests
{
    private static LumonSceneFeedbackRequestProcessor CreateProcessor(
        LumonScenePhysicalFieldPool pool,
        LumonScenePageTableEntry[] pageTable,
        Dictionary<ulong, uint> virtualToPhysical,
        Dictionary<uint, ulong> physicalToVirtual,
        ILumonScenePageTableWriter writer)
    {
        var stats = new LumonScenePageTableStatsTracker();
        int slots = Math.Max(1, pageTable.Length / LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk);
        stats.Reset(slots);
        return new LumonSceneFeedbackRequestProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writer, stats);
    }

    [Fact]
    public void NewAllocation_UpdatesMappingsPageTable_AndEmitsCaptureAndRelightWork()
    {
        using var pool = CreateNearPool(capacityNotClamped: true);

        var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<ulong, uint>();
        var physicalToVirtual = new Dictionary<uint, ulong>();
        var writes = new RecordingPageTableWriter();

        var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

        var requests = new[]
        {
            new LumonScenePageRequestGpu(chunkSlot: 0u, virtualPageIndex: 5u, mip: 0u, flags: 123u),
        };

        var capture = new LumonSceneCaptureWorkGpu[8];
        var relight = new LumonSceneRelightWorkGpu[8];

        int recaptureCursor = 0;
        proc.Process(
            requests: requests,
            maxRequestsToProcess: 1024,
            maxNewAllocations: 16,
            maxPagesPerChunkSlot: 1024,
            recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out int captureCount,
            relightCount: out int relightCount,
            stats: out _);

        Assert.Equal(1, captureCount);
        Assert.Equal(1, relightCount);

        ulong key = LumonSceneVirtualPageKeyUtil.Pack(chunkSlot: 0u, virtualPageIndex: 5u);
        Assert.True(virtualToPhysical.TryGetValue(key, out uint pid));
        Assert.NotEqual(0u, pid);
        Assert.True(physicalToVirtual.TryGetValue(pid, out ulong outKey));
        Assert.Equal(key, outKey);

        uint packed = pageTable[5].Packed;
        Assert.Equal(pid, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[5]));
        var flags = LumonScenePageTableEntryPacking.UnpackFlags(pageTable[5]);
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.Resident));
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.NeedsCapture));
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.NeedsRelight));

        Assert.Contains(writes.Writes, w => w.VirtualPageIndex == 5 && w.PackedEntry == packed);

        Assert.Equal(pid, capture[0].PhysicalPageId);
        Assert.Equal(0u, capture[0].ChunkSlot);
        Assert.Equal(123u, capture[0].PatchId);
        Assert.Equal(5u, capture[0].VirtualPageIndex);

        Assert.Equal(pid, relight[0].PhysicalPageId);
        Assert.Equal(0u, relight[0].ChunkSlot);
        Assert.Equal(123u, relight[0].PatchId);
        Assert.Equal(5u, relight[0].VirtualPageIndex);
    }

    [Fact]
    public void DuplicateRequest_DoesNotAllocateNewPage_AndTouchesMRU()
    {
        using var pool = CreateNearPool(capacityNotClamped: true);

        var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<ulong, uint>();
        var physicalToVirtual = new Dictionary<uint, ulong>();
        var writes = new RecordingPageTableWriter();

        var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

        var capture = new LumonSceneCaptureWorkGpu[16];
        var relight = new LumonSceneRelightWorkGpu[16];
        int recaptureCursor = 0;

        // Allocate two pages: vpage 1 then vpage 2 (vpage 2 is MRU).
        proc.Process(
            requests: new[]
            {
                new LumonScenePageRequestGpu(0u, 1u, 0u, 100u),
                new LumonScenePageRequestGpu(0u, 2u, 0u, 200u),
            },
            maxRequestsToProcess: 1024,
            maxNewAllocations: 16,
            maxPagesPerChunkSlot: 1024,
            recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out _,
            relightCount: out _,
            stats: out _);

        ulong key1 = LumonSceneVirtualPageKeyUtil.Pack(chunkSlot: 0u, virtualPageIndex: 1u);
        ulong key2 = LumonSceneVirtualPageKeyUtil.Pack(chunkSlot: 0u, virtualPageIndex: 2u);
        uint pid1 = virtualToPhysical[key1];
        uint pid2 = virtualToPhysical[key2];

        // Touch vpage 1 again; should become MRU.
        proc.Process(
            requests: new[] { new LumonScenePageRequestGpu(0u, 1u, 0u, 999u) },
            maxRequestsToProcess: 1024,
            maxNewAllocations: 0,
            maxPagesPerChunkSlot: 1024,
            recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out int captureCount,
            relightCount: out int relightCount,
            stats: out _);

        Assert.Equal(0, captureCount);
        Assert.Equal(0, relightCount);
        Assert.Equal(pid1, virtualToPhysical[key1]);
        Assert.Equal(pid2, virtualToPhysical[key2]);

        Span<uint> mru = stackalloc uint[2];
        int written = pool.PagePool.CopyMostRecentlyUsed(mru);
        Assert.Equal(2, written);
        Assert.Equal(pid1, mru[0]);
        Assert.Equal(pid2, mru[1]);
    }

    [Fact]
    public void Budgets_ClampNewAllocations_AndRequestsProcessed()
    {
        using var pool = CreateNearPool(capacityNotClamped: true);

        var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<ulong, uint>();
        var physicalToVirtual = new Dictionary<uint, ulong>();
        var writes = new RecordingPageTableWriter();
        var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

        var req = new LumonScenePageRequestGpu[10];
        for (int i = 0; i < req.Length; i++)
        {
            req[i] = new LumonScenePageRequestGpu(0u, (uint)i, 0u, (uint)(1000 + i));
        }

        var capture = new LumonSceneCaptureWorkGpu[16];
        var relight = new LumonSceneRelightWorkGpu[16];
        int recaptureCursor = 0;

        proc.Process(
            requests: req,
            maxRequestsToProcess: 5,
            maxNewAllocations: 2,
            maxPagesPerChunkSlot: 1024,
            recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out int captureCount,
            relightCount: out int relightCount,
            stats: out _);

        Assert.Equal(2, captureCount);
        Assert.Equal(2, relightCount);
        Assert.Equal(2, virtualToPhysical.Count);

        // Only the first 5 requests were eligible; with maxNewAllocations=2, we should have allocated among vpages [0..4].
        foreach (ulong key in virtualToPhysical.Keys)
        {
            int vpage = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
            Assert.InRange(vpage, 0, 4);
        }
    }

    [Fact]
    public void WhenPoolSaturated_DoesNotEvictExistingPages_AndSkipsNewAllocations()
    {
        using var pool = CreateNearPool(capacityNotClamped: false); // very small capacity to force saturation

        var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<ulong, uint>();
        var physicalToVirtual = new Dictionary<uint, ulong>();
        var writes = new RecordingPageTableWriter();
        var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

        var req = new LumonScenePageRequestGpu[6];
        for (int i = 0; i < req.Length; i++)
        {
            req[i] = new LumonScenePageRequestGpu(0u, (uint)i, 0u, (uint)(10 + i));
        }

        var capture = new LumonSceneCaptureWorkGpu[32];
        var relight = new LumonSceneRelightWorkGpu[32];
        int recaptureCursor = 0;

        proc.Process(
            requests: req,
            maxRequestsToProcess: 1024,
            maxNewAllocations: 1024,
            maxPagesPerChunkSlot: 1024,
            recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out _,
            relightCount: out _,
            stats: out _);

        // Capacity is 4 pages; after 6 unique requests, the first 4 should remain allocated and the last 2 should be skipped.
        Assert.Equal(4, virtualToPhysical.Count);
        Assert.True(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 0u)));
        Assert.True(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 1u)));
        Assert.True(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 2u)));
        Assert.True(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 3u)));
        Assert.False(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 4u)));
        Assert.False(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 5u)));

        Assert.NotEqual(0u, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[0]));
        Assert.NotEqual(0u, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[1]));
        Assert.NotEqual(0u, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[2]));
        Assert.NotEqual(0u, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[3]));
        Assert.Equal(0u, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[4]));
        Assert.Equal(0u, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[5]));

        Assert.DoesNotContain(writes.Writes, w => w.VirtualPageIndex == 0 && w.PackedEntry == 0u);
        Assert.DoesNotContain(writes.Writes, w => w.VirtualPageIndex == 1 && w.PackedEntry == 0u);
    }

    [Fact]
    public void WhenPoolSaturated_DoesNotEvictAcrossChunkSlots()
    {
        using var pool = CreateNearPool(capacityNotClamped: false); // very small capacity to force saturation

        var pageTable = new LumonScenePageTableEntry[checked(LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * 2)];
        var virtualToPhysical = new Dictionary<ulong, uint>();
        var physicalToVirtual = new Dictionary<uint, ulong>();
        var writes = new RecordingPageTableWriter();
        var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

        // Fill the pool with 4 unique pages across two chunkSlots.
        var initial = new[]
        {
            new LumonScenePageRequestGpu(chunkSlot: 0u, virtualPageIndex: 0u, mip: 0u, flags: 10u),
            new LumonScenePageRequestGpu(chunkSlot: 0u, virtualPageIndex: 1u, mip: 0u, flags: 11u),
            new LumonScenePageRequestGpu(chunkSlot: 1u, virtualPageIndex: 0u, mip: 0u, flags: 20u),
            new LumonScenePageRequestGpu(chunkSlot: 1u, virtualPageIndex: 1u, mip: 0u, flags: 21u),
        };

        var capture = new LumonSceneCaptureWorkGpu[32];
        var relight = new LumonSceneRelightWorkGpu[32];
        int recaptureCursor = 0;

        proc.Process(
            requests: initial,
            maxRequestsToProcess: 1024,
            maxNewAllocations: 1024,
            maxPagesPerChunkSlot: 1024,
            recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out _,
            relightCount: out _,
            stats: out _);

        Assert.Equal(4, virtualToPhysical.Count);

        // Now request additional unique pages; they should be skipped (no eviction), preserving earlier allocations.
        var saturated = new[]
        {
            new LumonScenePageRequestGpu(chunkSlot: 0u, virtualPageIndex: 2u, mip: 0u, flags: 12u),
            new LumonScenePageRequestGpu(chunkSlot: 1u, virtualPageIndex: 2u, mip: 0u, flags: 22u),
            new LumonScenePageRequestGpu(chunkSlot: 0u, virtualPageIndex: 3u, mip: 0u, flags: 13u),
            new LumonScenePageRequestGpu(chunkSlot: 1u, virtualPageIndex: 3u, mip: 0u, flags: 23u),
        };

        proc.Process(
            requests: saturated,
            maxRequestsToProcess: 1024,
            maxNewAllocations: 1024,
            maxPagesPerChunkSlot: 1024,
            recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out _,
            relightCount: out _,
            stats: out _);

        Assert.Equal(4, virtualToPhysical.Count);

        Assert.True(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 0u)));
        Assert.True(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 1u)));
        Assert.True(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(1u, 0u)));
        Assert.True(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(1u, 1u)));

        Assert.False(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 2u)));
        Assert.False(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(1u, 2u)));
        Assert.False(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(0u, 3u)));
        Assert.False(virtualToPhysical.ContainsKey(LumonSceneVirtualPageKeyUtil.Pack(1u, 3u)));

        Assert.DoesNotContain(writes.Writes, w => w.PackedEntry == 0u);
    }

    [Fact]
    public void Converges_AllocatingKNewPagesPerFrame_UntilSatisfiedOrCapacity()
    {
        using var pool = CreateNearPool(capacityNotClamped: true);

        var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<ulong, uint>();
        var physicalToVirtual = new Dictionary<uint, ulong>();
        var writes = new RecordingPageTableWriter();
        var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

        const int totalDistinct = 10;
        var req = new LumonScenePageRequestGpu[totalDistinct];
        for (int i = 0; i < totalDistinct; i++)
        {
            req[i] = new LumonScenePageRequestGpu(0u, (uint)(100 + i), 0u, (uint)(2000 + i));
        }

        var capture = new LumonSceneCaptureWorkGpu[64];
        var relight = new LumonSceneRelightWorkGpu[64];
        int recaptureCursor = 0;

        const int k = 2;
        for (int frame = 0; frame < 8; frame++)
        {
            proc.Process(
                requests: req,
                maxRequestsToProcess: 1024,
                maxNewAllocations: k,
                maxPagesPerChunkSlot: 1024,
                recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
                recaptureCursor: ref recaptureCursor,
                maxRecapture: 0,
                captureWorkOut: capture,
                relightWorkOut: relight,
                captureCount: out _,
                relightCount: out _,
                stats: out _);

            if (virtualToPhysical.Count >= totalDistinct)
            {
                break;
            }
        }

        Assert.Equal(totalDistinct, virtualToPhysical.Count);
    }

    [Fact]
    public void SlotStats_ReportBudgetStarvationAndAllocationFailures()
    {
        // Global allocation budget starvation.
        {
            using var pool = CreateNearPool(capacityNotClamped: true);

            var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
            var virtualToPhysical = new Dictionary<ulong, uint>();
            var physicalToVirtual = new Dictionary<uint, ulong>();
            var writes = new RecordingPageTableWriter();
            var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

            var req = new LumonScenePageRequestGpu[10];
            for (int i = 0; i < req.Length; i++)
            {
                req[i] = new LumonScenePageRequestGpu(0u, (uint)i, 0u, 1u);
            }

            var capture = new LumonSceneCaptureWorkGpu[16];
            var relight = new LumonSceneRelightWorkGpu[16];
            int recaptureCursor = 0;

            Span<ushort> newAllocs = stackalloc ushort[1];
            Span<ushort> skippedBudget = stackalloc ushort[1];
            Span<ushort> skippedSlotBudget = stackalloc ushort[1];
            Span<ushort> allocFailures = stackalloc ushort[1];

            proc.Process(
                requests: req,
                maxRequestsToProcess: 1024,
                maxNewAllocations: 2,
                maxPagesPerChunkSlot: 1024,
                recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
                recaptureCursor: ref recaptureCursor,
                maxRecapture: 0,
                captureWorkOut: capture,
                relightWorkOut: relight,
                captureCount: out _,
                relightCount: out _,
                stats: out _,
                newAllocationsByChunkSlot: newAllocs,
                skippedGlobalBudgetByChunkSlot: skippedBudget,
                skippedChunkSlotBudgetByChunkSlot: skippedSlotBudget,
                allocationFailuresByChunkSlot: allocFailures);

            Assert.Equal((ushort)2, newAllocs[0]);
            Assert.Equal((ushort)8, skippedBudget[0]);
            Assert.Equal((ushort)0, skippedSlotBudget[0]);
            Assert.Equal((ushort)0, allocFailures[0]);
        }

        // Per-slot budget starvation.
        {
            using var pool = CreateNearPool(capacityNotClamped: true);

            var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
            var virtualToPhysical = new Dictionary<ulong, uint>();
            var physicalToVirtual = new Dictionary<uint, ulong>();
            var writes = new RecordingPageTableWriter();
            var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

            var req = new[]
            {
                new LumonScenePageRequestGpu(0u, 0u, 0u, 1u),
                new LumonScenePageRequestGpu(0u, 1u, 0u, 1u),
                new LumonScenePageRequestGpu(0u, 2u, 0u, 1u),
            };

            var capture = new LumonSceneCaptureWorkGpu[16];
            var relight = new LumonSceneRelightWorkGpu[16];
            int recaptureCursor = 0;

            Span<ushort> newAllocs = stackalloc ushort[1];
            Span<ushort> skippedBudget = stackalloc ushort[1];
            Span<ushort> skippedSlotBudget = stackalloc ushort[1];
            Span<ushort> allocFailures = stackalloc ushort[1];

            proc.Process(
                requests: req,
                maxRequestsToProcess: 1024,
                maxNewAllocations: 1024,
                maxPagesPerChunkSlot: 1,
                recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
                recaptureCursor: ref recaptureCursor,
                maxRecapture: 0,
                captureWorkOut: capture,
                relightWorkOut: relight,
                captureCount: out _,
                relightCount: out _,
                stats: out _,
                newAllocationsByChunkSlot: newAllocs,
                skippedGlobalBudgetByChunkSlot: skippedBudget,
                skippedChunkSlotBudgetByChunkSlot: skippedSlotBudget,
                allocationFailuresByChunkSlot: allocFailures);

            Assert.Equal((ushort)1, newAllocs[0]);
            Assert.Equal((ushort)0, skippedBudget[0]);
            Assert.Equal((ushort)2, skippedSlotBudget[0]);
            Assert.Equal((ushort)0, allocFailures[0]);
        }

        // Allocation failures (pool saturated, no eviction).
        {
            using var pool = CreateNearPool(capacityNotClamped: false);

            var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
            var virtualToPhysical = new Dictionary<ulong, uint>();
            var physicalToVirtual = new Dictionary<uint, ulong>();
            var writes = new RecordingPageTableWriter();
            var proc = CreateProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

            var req = new LumonScenePageRequestGpu[6];
            for (int i = 0; i < req.Length; i++)
            {
                req[i] = new LumonScenePageRequestGpu(0u, (uint)i, 0u, 1u);
            }

            var capture = new LumonSceneCaptureWorkGpu[32];
            var relight = new LumonSceneRelightWorkGpu[32];
            int recaptureCursor = 0;

            Span<ushort> newAllocs = stackalloc ushort[1];
            Span<ushort> skippedBudget = stackalloc ushort[1];
            Span<ushort> skippedSlotBudget = stackalloc ushort[1];
            Span<ushort> allocFailures = stackalloc ushort[1];

            proc.Process(
                requests: req,
                maxRequestsToProcess: 1024,
                maxNewAllocations: 1024,
                maxPagesPerChunkSlot: 1024,
                recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
                recaptureCursor: ref recaptureCursor,
                maxRecapture: 0,
                captureWorkOut: capture,
                relightWorkOut: relight,
                captureCount: out _,
                relightCount: out _,
                stats: out _,
                newAllocationsByChunkSlot: newAllocs,
                skippedGlobalBudgetByChunkSlot: skippedBudget,
                skippedChunkSlotBudgetByChunkSlot: skippedSlotBudget,
                allocationFailuresByChunkSlot: allocFailures);

            Assert.Equal((ushort)4, newAllocs[0]);
            Assert.Equal((ushort)0, skippedBudget[0]);
            Assert.Equal((ushort)0, skippedSlotBudget[0]);
            Assert.Equal((ushort)2, allocFailures[0]);
        }
    }

    private static LumonScenePhysicalFieldPool CreateNearPool(bool capacityNotClamped)
    {
        // Use deterministic CPU-only pools; no GPU resources created.
        var pool = new LumonScenePhysicalFieldPool(LumonSceneField.Near);

        // capacityNotClamped=true yields a capacity >= 15 pages (nearRadiusChunks=1, tileSize=16 => huge tilesPerAtlas).
        // capacityNotClamped=false yields a tiny capacity (tileSize=2048 => 2x2 tiles => 4 pages) forcing saturation.
        int texelsPerVoxelFaceEdge = capacityNotClamped ? 4 : 512;
        LumonScenePhysicalPoolPlan plan = LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: texelsPerVoxelFaceEdge,
            nearRadiusXZChunks: 1,
            nearRadiusYChunks: 0,
            nearPagesPerChunkBudget: 1,
            maxAtlasCount: 1);

        pool.Configure(plan);
        return pool;
    }

    private sealed class RecordingPageTableWriter : ILumonScenePageTableWriter
    {
        public readonly List<Write> Writes = new();

        public void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry)
        {
            Writes.Add(new Write(chunkSlot, virtualPageIndex, packedEntry));
        }

        public readonly record struct Write(int ChunkSlot, int VirtualPageIndex, uint PackedEntry);
    }
}
