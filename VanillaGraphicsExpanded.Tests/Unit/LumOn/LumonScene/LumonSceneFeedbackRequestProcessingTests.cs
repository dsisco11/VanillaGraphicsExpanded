using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.LumOn.Scene;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneFeedbackRequestProcessingTests
{
    [Fact]
    public void NewAllocation_UpdatesMappingsPageTable_AndEmitsCaptureAndRelightWork()
    {
        using var pool = CreateNearPool(capacityNotClamped: true);

        var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<int, uint>();
        var physicalToVirtual = new Dictionary<uint, int>();
        var writes = new RecordingPageTableWriter();

        var proc = new LumonSceneFeedbackRequestProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

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
            recaptureVirtualPages: ReadOnlySpan<int>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out int captureCount,
            relightCount: out int relightCount);

        Assert.Equal(1, captureCount);
        Assert.Equal(1, relightCount);

        Assert.True(virtualToPhysical.TryGetValue(5, out uint pid));
        Assert.NotEqual(0u, pid);
        Assert.True(physicalToVirtual.TryGetValue(pid, out int vpage));
        Assert.Equal(5, vpage);

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
        var virtualToPhysical = new Dictionary<int, uint>();
        var physicalToVirtual = new Dictionary<uint, int>();
        var writes = new RecordingPageTableWriter();

        var proc = new LumonSceneFeedbackRequestProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

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
            recaptureVirtualPages: ReadOnlySpan<int>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out _,
            relightCount: out _);

        uint pid1 = virtualToPhysical[1];
        uint pid2 = virtualToPhysical[2];

        // Touch vpage 1 again; should become MRU.
        proc.Process(
            requests: new[] { new LumonScenePageRequestGpu(0u, 1u, 0u, 999u) },
            maxRequestsToProcess: 1024,
            maxNewAllocations: 0,
            recaptureVirtualPages: ReadOnlySpan<int>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out int captureCount,
            relightCount: out int relightCount);

        Assert.Equal(0, captureCount);
        Assert.Equal(0, relightCount);
        Assert.Equal(pid1, virtualToPhysical[1]);
        Assert.Equal(pid2, virtualToPhysical[2]);

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
        var virtualToPhysical = new Dictionary<int, uint>();
        var physicalToVirtual = new Dictionary<uint, int>();
        var writes = new RecordingPageTableWriter();
        var proc = new LumonSceneFeedbackRequestProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

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
            recaptureVirtualPages: ReadOnlySpan<int>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out int captureCount,
            relightCount: out int relightCount);

        Assert.Equal(2, captureCount);
        Assert.Equal(2, relightCount);
        Assert.Equal(2, virtualToPhysical.Count);

        // Only the first 5 requests were eligible; with maxNewAllocations=2, we should have allocated among vpages [0..4].
        foreach (int vpage in virtualToPhysical.Keys)
        {
            Assert.InRange(vpage, 0, 4);
        }
    }

    [Fact]
    public void Eviction_UnderPressure_ClearsOldVirtualPageEntry()
    {
        using var pool = CreateNearPool(capacityNotClamped: false); // very small capacity to force eviction

        var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<int, uint>();
        var physicalToVirtual = new Dictionary<uint, int>();
        var writes = new RecordingPageTableWriter();
        var proc = new LumonSceneFeedbackRequestProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

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
            recaptureVirtualPages: ReadOnlySpan<int>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: capture,
            relightWorkOut: relight,
            captureCount: out _,
            relightCount: out _);

        // Capacity is 4 pages; after 6 unique requests, the first two should have been evicted.
        Assert.Equal(4, virtualToPhysical.Count);
        Assert.False(virtualToPhysical.ContainsKey(0));
        Assert.False(virtualToPhysical.ContainsKey(1));
        Assert.True(virtualToPhysical.ContainsKey(2));
        Assert.True(virtualToPhysical.ContainsKey(3));
        Assert.True(virtualToPhysical.ContainsKey(4));
        Assert.True(virtualToPhysical.ContainsKey(5));

        Assert.Equal(0u, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[0]));
        Assert.Equal(0u, LumonScenePageTableEntryPacking.UnpackPhysicalPageId(pageTable[1]));

        Assert.Contains(writes.Writes, w => w.VirtualPageIndex == 0 && w.PackedEntry == 0u);
        Assert.Contains(writes.Writes, w => w.VirtualPageIndex == 1 && w.PackedEntry == 0u);
    }

    [Fact]
    public void Converges_AllocatingKNewPagesPerFrame_UntilSatisfiedOrCapacity()
    {
        using var pool = CreateNearPool(capacityNotClamped: true);

        var pageTable = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<int, uint>();
        var physicalToVirtual = new Dictionary<uint, int>();
        var writes = new RecordingPageTableWriter();
        var proc = new LumonSceneFeedbackRequestProcessor(pool, pageTable, virtualToPhysical, physicalToVirtual, writes);

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
                recaptureVirtualPages: ReadOnlySpan<int>.Empty,
                recaptureCursor: ref recaptureCursor,
                maxRecapture: 0,
                captureWorkOut: capture,
                relightWorkOut: relight,
                captureCount: out _,
                relightCount: out _);

            if (virtualToPhysical.Count >= totalDistinct)
            {
                break;
            }
        }

        Assert.Equal(totalDistinct, virtualToPhysical.Count);
    }

    private static LumonScenePhysicalFieldPool CreateNearPool(bool capacityNotClamped)
    {
        // Use deterministic CPU-only pools; no GPU resources created.
        var pool = new LumonScenePhysicalFieldPool(LumonSceneField.Near);

        // capacityNotClamped=true yields a capacity >= 15 pages (nearRadiusChunks=1, tileSize=16 => huge tilesPerAtlas).
        // capacityNotClamped=false yields a tiny capacity (tileSize=2048 => 2x2 tiles => 4 pages) forcing eviction.
        int texelsPerVoxelFaceEdge = capacityNotClamped ? 4 : 512;
        LumonScenePhysicalPoolPlan plan = LumonScenePhysicalPoolPlanner.CreateNearPlan(
            nearTexelsPerVoxelFaceEdge: texelsPerVoxelFaceEdge,
            nearRadiusChunks: 1,
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

