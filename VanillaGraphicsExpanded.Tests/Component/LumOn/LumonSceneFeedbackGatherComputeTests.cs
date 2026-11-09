using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises lumon scene feedback gather compute behavior using the test harness.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumonSceneFeedbackGatherComputeTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context to verify feedback request gathering and compaction.</summary>
    public LumonSceneFeedbackGatherComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Verifies scan offsets distribute bounded compacted feedback across chunk slots.</summary>

    [Fact]
    public void FeedbackCompact_ScanOffset_DistributesBoundedRequestsAcrossChunkSlots()
    {
        // Arrange a stamp texture containing requests across every chunk slot and cap the compact output.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactShaderOwner, out string compactShaderLog), compactShaderLog);
        using var compactShader = compactShaderOwner!;

        const int chunkSlotCount = 8;
        const uint frameStamp = 1u;
        const uint maxRequests = 32u;

        // Pretend "everything is visible": stamp every (chunkSlot, vpage) as used in frameStamp.
        using var usageStamp = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageUsageStamp" }).RequireResource();
        uint[] data = new uint[128 * 128 * chunkSlotCount];
        Array.Fill(data, frameStamp);
        usageStamp.UploadDataImmediate(data, 0, 0, 0, 128, 128, chunkSlotCount);

        // Page table is required by the compaction shader (filters mapped vs unmapped pages).
        // For this test, keep it all-zero so every stamped page is treated as unmapped.
        using var pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageTableMip0" }).RequireResource();
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)maxRequests, sentinel: Sentinel);
        using var counter = new ComponentAtomicCounters(initialValue: 0u);

        var seen = new bool[chunkSlotCount];

        compactShader.BindRequestCounter(counter.Buffer);
        compactShader.BindRequestsSsbo(requests.Buffer);
        compactShader.PageUsageStamp = usageStamp;
        compactShader.PageTableMip0 = pageTableMip0;

        // Verify scanOffset rotation: offset increments by VirtualPagesPerChunk each "frame"
        // and should rotate the dominant slot for the bounded request list.
        const uint virtualPagesPerChunk = 128u * 128u;
        for (uint f = 0; f < (uint)chunkSlotCount; f++)
        {
            counter.Upload(value: 0u);

            compactShader.MaxRequests = maxRequests;
            compactShader.FrameStamp = frameStamp;
            compactShader.ScanOffset = f * virtualPagesPerChunk;
            compactShader.CompactMode = 1u;

            // Dispatch only a single workgroup so the outcome is deterministic (no cross-workgroup atomic ordering).
            // This validates the scanOffset mapping logic itself (slot rotation), which is the core fairness mechanism.
            // Act by scanning the requested offset into the bounded request buffer.
            compactShader.Dispatch(1, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            GpuTestFence.WaitForGpuOrSkip($"FeedbackCompact scanOffset frame={f}");

            uint written = counter.Read();
            // Assert the compacted count and returned slot ordering follow the scan offset.
            Assert.Equal(256u, written);

            RequestGpu[] outReq = requests.ReadBack(count: (int)maxRequests);
            uint expectedSlot = f % (uint)chunkSlotCount;

            Assert.True(outReq.Length > 0);

            // The first maxRequests entries should be dominated by the expected slot for this offset.
            for (int i = 0; i < outReq.Length; i++)
            {
                Assert.Equal(expectedSlot, outReq[i].ChunkSlot);
            }

            seen[expectedSlot] = true;
        }

        for (int s = 0; s < seen.Length; s++)
        {
            Assert.True(seen[s], $"Expected scanOffset to eventually return requests for chunkSlot={s}.");
        }

    }

    /// <summary>Verifies stamp-texture feedback gathering emits each page request once.</summary>

    [Fact]
    public void FeedbackGather_DeduplicatesViaStampTexture_AndEmitsUniqueRequests()
    {
        // Arrange duplicate patch requests, a fresh stamp texture, and the mark/compact shader pair.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var markShaderOwner, out string markShaderLog), markShaderLog);
        using var markShader = markShaderOwner!;
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactShaderOwner, out string compactShaderLog), compactShaderLog);

        using var compactShader = compactShaderOwner!;
        const int w = 8;
        const int h = 8;

        using var patchId = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PatchIdGBuffer" }).RequireResource();

        const int chunkSlotCount = 8;
        using var usageStamp = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageUsageStamp" }).RequireResource();
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageTableMip0" }).RequireResource();
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var genTex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = System.Math.Max(1, chunkSlotCount), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_ChunkSlotGeneration" }).RequireResource();
        genTex.UploadDataImmediate(new uint[chunkSlotCount], x: 0, y: 0, regionWidth: chunkSlotCount, regionHeight: 1);

        uint[] pid = new uint[w * h * 4];
        void Set(int x, int y, uint chunkSlot, uint patch)
        {
            int idx = (y * w + x) * 4;
            pid[idx + 0] = chunkSlot;
            pid[idx + 1] = patch;
            pid[idx + 2] = 0u;
            pid[idx + 3] = 0u; // generation16
        }

        // patchId==0 should be ignored.
        Set(0, 0, chunkSlot: 0u, patch: 0u);

        // A few sparse requests.
        Set(1, 0, chunkSlot: 0u, patch: 1u);
        Set(2, 3, chunkSlot: 0u, patch: 777u);
        Set(7, 7, chunkSlot: 0u, patch: 12000u);

        // Non-zero chunk slot should be supported by v3 gather.
        Set(3, 3, chunkSlot: 5u, patch: 2u);

        patchId.UploadDataImmediate(pid);

        const uint capacity = 64;
        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)capacity, sentinel: Sentinel);

        using var counter = new ComponentAtomicCounters(initialValue: 0u);
        using var markCounters = new ComponentAtomicCounters(counterCount: 8);
        markShader.BindDebugCounters(markCounters.Buffer);
        // Pass A: mark pages.
        markShader.PatchIdG = patchId;
        markShader.ChunkSlotGenerationTex = genTex;
        markShader.FrameStamp = 1u;
        markShader.BindPageUsageStampImage(usageStamp, access: ImageAccess.ReadWrite);
        // Act stage one marks each virtual page once in the per-frame usage texture.
        markShader.Dispatch((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Pass B: compact stamps -> unique requests list.
        compactShader.BindRequestCounter(counter.Buffer);
        compactShader.BindRequestsSsbo(requests.Buffer);
        compactShader.PageUsageStamp = usageStamp;
        compactShader.PageTableMip0 = pageTableMip0;
        compactShader.MaxRequests = capacity;
        compactShader.FrameStamp = 1u;
        compactShader.ScanOffset = 0u;
        compactShader.CompactMode = 1u;
        // Act stage two compacts the marked pages into requests for CPU processing.
        compactShader.Dispatch((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("FeedbackGather compact pass (dedup unique requests)");

        uint requestCount = counter.Read();
        // Assert four unique requests survive and every expected request appears once.
        Assert.Equal(4u, requestCount);

        RequestGpu[] outReq = requests.ReadBack(count: (int)requestCount);

        var expected = new Dictionary<RequestGpu, int>(RequestGpuComparer.Instance)
        {
            [new RequestGpu(0u, 1u, 0u, 1u)] = 1,
            [new RequestGpu(0u, 777u, 0u, 777u)] = 1,
            [new RequestGpu(0u, 12000u, 0u, 12000u)] = 1,
            [new RequestGpu(5u, 2u, 0u, 2u)] = 1,
        };

        foreach (var req in outReq)
        {
            Assert.True(expected.TryGetValue(req, out int left) && left > 0, $"Unexpected request: {req}");
            expected[req] = left - 1;
        }

        foreach (var kvp in expected)
        {
            Assert.Equal(0, kvp.Value);
        }

    }

    /// <summary>Verifies the mark pass rejects feedback from an obsolete generation.</summary>

    [Fact]
    public void FeedbackGather_GenerationMismatch_IsRejectedInMarkPass()
    {
        // Arrange a page request whose chunk-slot generation disagrees with the generation texture.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var markShaderOwner, out string markShaderLog), markShaderLog);
        using var markShader = markShaderOwner!;
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactShaderOwner, out string compactShaderLog), compactShaderLog);

        using var compactShader = compactShaderOwner!;
        const int w = 8;
        const int h = 8;

        const int chunkSlotCount = 2;

        using var patchId = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PatchIdGBuffer" }).RequireResource();
        using var usageStamp = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageUsageStamp" }).RequireResource();
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageTableMip0" }).RequireResource();
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var genTex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = System.Math.Max(1, chunkSlotCount), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_ChunkSlotGeneration" }).RequireResource();
        genTex.UploadDataImmediate(new uint[] { 1u, 2u }, x: 0, y: 0, regionWidth: chunkSlotCount, regionHeight: 1);

        uint[] pid = new uint[w * h * 4];
        void Set(int x, int y, uint chunkSlot, uint patch, uint gen16)
        {
            int idx = (y * w + x) * 4;
            pid[idx + 0] = chunkSlot;
            pid[idx + 1] = patch;
            pid[idx + 2] = 0u;
            pid[idx + 3] = gen16;
        }

        // One valid pixel (slot 0, generation 1).
        Set(0, 0, chunkSlot: 0u, patch: 1u, gen16: 1u);

        // One stale pixel (slot 1 wants generation 2, but PatchId says 1).
        Set(1, 0, chunkSlot: 1u, patch: 1u, gen16: 1u);

        patchId.UploadDataImmediate(pid);

        const uint capacity = 16;
        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)capacity, sentinel: Sentinel);
        using var markCounters = new ComponentAtomicCounters(initialValue: 0u, counterCount: 3);

        using var counter = new ComponentAtomicCounters(initialValue: 0u);
        markShader.BindDebugCounters(markCounters.Buffer);
        markShader.PatchIdG = patchId;
        markShader.ChunkSlotGenerationTex = genTex;
        markShader.FrameStamp = 1u;
        markShader.BindPageUsageStampImage(usageStamp, access: ImageAccess.ReadWrite);
        // Act stage one rejects stale ownership while marking the page-use stamps.
        markShader.Dispatch((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        compactShader.BindRequestCounter(counter.Buffer);
        compactShader.BindRequestsSsbo(requests.Buffer);
        compactShader.PageUsageStamp = usageStamp;
        compactShader.PageTableMip0 = pageTableMip0;
        compactShader.MaxRequests = capacity;
        compactShader.FrameStamp = 1u;
        compactShader.ScanOffset = 0u;
        compactShader.CompactMode = 1u;
        // Act stage two compacts only the current-generation request.
        compactShader.Dispatch((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("FeedbackGather compact pass (generation mismatch)");

        uint requestCount = counter.Read();
        // Assert the stale request is absent and the valid page identity remains.
        Assert.Equal(1u, requestCount);

        RequestGpu[] outReq = requests.ReadBack(count: (int)requestCount);
        Assert.Equal(new RequestGpu(0u, 1u, 0u, 1u), outReq[0]);

    }

    /// <summary>Verifies duplicate patch identifiers produce a single feedback request.</summary>

    [Fact]
    public void FeedbackGather_DeduplicatesDuplicatePatchIds()
    {
        // Arrange two pixels that share one patch ID and a fresh per-frame stamp texture.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var markShaderOwner, out string markShaderLog), markShaderLog);
        using var markShader = markShaderOwner!;
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactShaderOwner, out string compactShaderLog), compactShaderLog);

        using var compactShader = compactShaderOwner!;
        const int w = 16;
        const int h = 16;


        using var patchId = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PatchIdGBuffer" }).RequireResource();
        const int chunkSlotCount = 1;
        using var usageStamp = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageUsageStamp" }).RequireResource();
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageTableMip0" }).RequireResource();
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var genTex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = System.Math.Max(1, chunkSlotCount), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_ChunkSlotGeneration" }).RequireResource();
        genTex.UploadDataImmediate(new uint[chunkSlotCount], x: 0, y: 0, regionWidth: chunkSlotCount, regionHeight: 1);

        uint[] pid = new uint[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            pid[i * 4 + 0] = 0u;
            pid[i * 4 + 1] = 777u;
            pid[i * 4 + 2] = 0u;
            pid[i * 4 + 3] = 0u; // generation16
        }
        patchId.UploadDataImmediate(pid);

        const uint capacity = 16;

        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)capacity, sentinel: Sentinel);
        using var markCounters = new ComponentAtomicCounters(initialValue: 0u, counterCount: 3);
        using var counter = new ComponentAtomicCounters(initialValue: 0u);
        markShader.BindDebugCounters(markCounters.Buffer);
        markShader.PatchIdG = patchId;
        markShader.ChunkSlotGenerationTex = genTex;
        markShader.FrameStamp = 1u;
        markShader.BindPageUsageStampImage(usageStamp, access: ImageAccess.ReadWrite);
        // Act stage one marks the duplicated patch request.
        markShader.Dispatch((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        compactShader.BindRequestCounter(counter.Buffer);
        compactShader.BindRequestsSsbo(requests.Buffer);
        compactShader.PageUsageStamp = usageStamp;
        compactShader.PageTableMip0 = pageTableMip0;
        compactShader.MaxRequests = capacity;
        compactShader.FrameStamp = 1u;
        compactShader.ScanOffset = 0u;
        compactShader.CompactMode = 1u;
        // Act stage two emits the deduplicated request.
        compactShader.Dispatch((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("FeedbackGather compact pass (duplicate patchIds)");

        // Assert one output request retains the original chunk-slot, page, and patch identity.
        Assert.Equal(1u, counter.Read());
        RequestGpu[] outReq = requests.ReadBack(count: 1);
        Assert.Equal(new RequestGpu(0u, 777u, 0u, 777u), outReq[0]);

    }

    #endregion

    #region Private
    private static readonly RequestGpu Sentinel = new(0xFFFF_FFFFu, 0xFFFF_FFFFu, 0xFFFF_FFFFu, 0xFFFF_FFFFu);
    /// <summary>Stores one GPU feedback request and its page identity.</summary>

[StructLayout(LayoutKind.Sequential)]
    private readonly record struct RequestGpu(uint ChunkSlot, uint VirtualPageIndex, uint Mip, uint PatchId);

    /// <summary>Compares request payloads by every field written by the feedback shader.</summary>
    private sealed class RequestGpuComparer : IEqualityComparer<RequestGpu>
    {
        #region Public API
        /// <summary>Provides the single comparer shared by feedback request sorting and validation.</summary>
        public static readonly RequestGpuComparer Instance = new();
        /// <summary>Returns whether two GPU feedback requests have identical slot, page, mip, and patch values.</summary>
        public bool Equals(RequestGpu x, RequestGpu y)
            => x.ChunkSlot == y.ChunkSlot
                && x.VirtualPageIndex == y.VirtualPageIndex
                && x.Mip == y.Mip
                && x.PatchId == y.PatchId;
        /// <summary>Hashes all identity fields used by feedback request equality.</summary>
        public int GetHashCode(RequestGpu obj)
            => HashCode.Combine(obj.ChunkSlot, obj.VirtualPageIndex, obj.Mip, obj.PatchId);
        #endregion
    }

    /// <summary>Owns a typed view of the test's shader-storage buffer and bounds readback to its capacity.</summary>
    private sealed class Ssbo<T> : IDisposable where T : unmanaged
    {
        #region Private
        private readonly int capacityItems;
        private readonly GpuShaderStorageBuffer buffer;
        #endregion

        #region Public API
        /// <summary>Wraps the allocated buffer with its element capacity for typed readback.</summary>
        public Ssbo(GpuShaderStorageBuffer buffer, int capacityItems)
        {
            this.buffer = buffer;
            this.capacityItems = capacityItems;
        }

        /// <summary>Exposes the owned storage to the production shader binding method.</summary>
        public GpuShaderStorageBuffer Buffer => buffer;
        /// <summary>Maps up to <paramref name="count"/> typed elements and returns a managed copy of the buffer contents.</summary>
        public T[] ReadBack(int count)
        {
            count = Math.Clamp(count, 0, capacityItems);
            var dst = new T[count];
            using var mapped = buffer.MapRange<T>(dstOffsetBytes: 0, elementCount: count, access: BufferMapAccess.Read);
            if (!mapped.IsMapped)
            {
                return dst;
            }
            mapped.Span.CopyTo(dst);
            return dst;
        }
        /// <summary>Releases the resources owned by this helper.</summary>
        public void Dispose() => buffer.Dispose();
        #endregion
    }

    /// <summary>Allocates a typed shader-storage buffer and initializes every element to the sentinel value.</summary>
    private static Ssbo<T> CreateSsbo<T>(string debugName, int capacityItems, T sentinel) where T : unmanaged
    {
        var ssbo = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicRead, Label = debugName }).RequireResource();
        int bytes = checked(capacityItems * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);

        T[] init = new T[capacityItems];
        for (int i = 0; i < init.Length; i++) init[i] = sentinel;
        ssbo.UploadSubData(init, dstOffsetBytes: 0, byteCount: bytes);

        return new Ssbo<T>(ssbo, capacityItems);
    }

    #endregion

}
