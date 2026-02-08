using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.GPU.Shaders;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonSceneFeedbackGatherComputeTests : RenderTestBase
{
    public LumonSceneFeedbackGatherComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void FeedbackCompact_ScanOffset_DistributesBoundedRequestsAcrossChunkSlots()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var compactShader = new LumonSceneFeedbackCompactPagesShader(helper, debugName: "Tests.FeedbackGather.CompactOnly");
        int compactProgram = compactShader.ProgramId;

        const int chunkSlotCount = 8;
        const uint frameStamp = 1u;
        const uint maxRequests = 32u;

        // Pretend "everything is visible": stamp every (chunkSlot, vpage) as used in frameStamp.
        using var usageStamp = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageUsageStamp");
        uint[] data = new uint[128 * 128 * chunkSlotCount];
        Array.Fill(data, frameStamp);
        usageStamp.UploadDataImmediate(data, 0, 0, 0, 128, 128, chunkSlotCount);

        // Page table is required by the compaction shader (filters mapped vs unmapped pages).
        // For this test, keep it all-zero so every stamped page is treated as unmapped.
        using var pageTableMip0 = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageTableMip0");
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)maxRequests, sentinel: Sentinel);
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        var seen = new bool[chunkSlotCount];

        compactShader.Use();
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);
        compactShader.BindPageUsageStamp(usageStamp.TextureId);
        compactShader.BindPageTableMip0(pageTableMip0.TextureId);

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
            GL.DispatchCompute(1, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            GpuTestFence.WaitForGpuOrSkip($"FeedbackCompact scanOffset frame={f}");

            uint written = counter.Read();
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

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void FeedbackGather_DeduplicatesViaStampTexture_AndEmitsUniqueRequests()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var markShader = new LumonSceneFeedbackMarkPagesShader(helper, debugName: "Tests.FeedbackGather.Mark");
        using var compactShader = new LumonSceneFeedbackCompactPagesShader(helper, debugName: "Tests.FeedbackGather.Compact");
        int markProgram = markShader.ProgramId;
        int compactProgram = compactShader.ProgramId;

        const int w = 8;
        const int h = 8;

        using var patchId = Texture2D.Create(w, h, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

        const int chunkSlotCount = 8;
        using var usageStamp = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageUsageStamp");
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageTableMip0");
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var genTex = Texture2D.Create(chunkSlotCount, 1, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, debugName: "Test_ChunkSlotGeneration");
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
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        // Pass A: mark pages.
        markShader.Use();
        markShader.BindPatchIdGBuffer(patchId.TextureId);
        markShader.BindChunkSlotGenerationTex(genTex.TextureId);
        markShader.FrameStamp = 1u;
        markShader.BindPageUsageStampImage(usageStamp.TextureId, access: TextureAccess.ReadWrite);
        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Pass B: compact stamps -> unique requests list.
        compactShader.Use();
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);
        compactShader.BindPageUsageStamp(usageStamp.TextureId);
        compactShader.BindPageTableMip0(pageTableMip0.TextureId);
        compactShader.MaxRequests = capacity;
        compactShader.FrameStamp = 1u;
        compactShader.ScanOffset = 0u;
        compactShader.CompactMode = 1u;
        GL.DispatchCompute((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("FeedbackGather compact pass (dedup unique requests)");

        uint requestCount = counter.Read();
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

        // Programs are disposed via ComputeProgram.
    }

    [Fact]
    public void FeedbackGather_GenerationMismatch_IsRejectedInMarkPass()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var markShader = new LumonSceneFeedbackMarkPagesShader(helper, debugName: "Tests.FeedbackGatherGenMismatch.Mark");
        using var compactShader = new LumonSceneFeedbackCompactPagesShader(helper, debugName: "Tests.FeedbackGatherGenMismatch.Compact");

        const int w = 8;
        const int h = 8;

        const int chunkSlotCount = 2;

        using var patchId = Texture2D.Create(w, h, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");
        using var usageStamp = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageUsageStamp");
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageTableMip0");
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var genTex = Texture2D.Create(chunkSlotCount, 1, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, debugName: "Test_ChunkSlotGeneration");
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
        using var markCounters = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 3);
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        markCounters.BindBase(bindingIndex: 0);
        markShader.Use();
        markShader.BindPatchIdGBuffer(patchId.TextureId);
        markShader.BindChunkSlotGenerationTex(genTex.TextureId);
        markShader.FrameStamp = 1u;
        markShader.BindPageUsageStampImage(usageStamp.TextureId, access: TextureAccess.ReadWrite);
        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);
        compactShader.Use();
        compactShader.BindPageUsageStamp(usageStamp.TextureId);
        compactShader.BindPageTableMip0(pageTableMip0.TextureId);
        compactShader.MaxRequests = capacity;
        compactShader.FrameStamp = 1u;
        compactShader.ScanOffset = 0u;
        compactShader.CompactMode = 1u;
        GL.DispatchCompute((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("FeedbackGather compact pass (generation mismatch)");

        uint requestCount = counter.Read();
        Assert.Equal(1u, requestCount);

        RequestGpu[] outReq = requests.ReadBack(count: (int)requestCount);
        Assert.Equal(new RequestGpu(0u, 1u, 0u, 1u), outReq[0]);

        // Programs are disposed via ComputeProgram.
    }

    [Fact]
    public void FeedbackGather_DeduplicatesDuplicatePatchIds()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var markShader = new LumonSceneFeedbackMarkPagesShader(helper, debugName: "Tests.FeedbackGatherDedup.Mark");
        using var compactShader = new LumonSceneFeedbackCompactPagesShader(helper, debugName: "Tests.FeedbackGatherDedup.Compact");

        const int w = 16;
        const int h = 16;

        using var patchId = Texture2D.Create(w, h, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

        const int chunkSlotCount = 1;
        using var usageStamp = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageUsageStamp");
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageTableMip0");
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var genTex = Texture2D.Create(chunkSlotCount, 1, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, debugName: "Test_ChunkSlotGeneration");
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
        using var markCounters = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 3);
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        markCounters.BindBase(bindingIndex: 0);
        markShader.Use();
        markShader.BindPatchIdGBuffer(patchId.TextureId);
        markShader.BindChunkSlotGenerationTex(genTex.TextureId);
        markShader.FrameStamp = 1u;
        markShader.BindPageUsageStampImage(usageStamp.TextureId, access: TextureAccess.ReadWrite);
        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);
        compactShader.Use();
        compactShader.BindPageUsageStamp(usageStamp.TextureId);
        compactShader.BindPageTableMip0(pageTableMip0.TextureId);
        compactShader.MaxRequests = capacity;
        compactShader.FrameStamp = 1u;
        compactShader.ScanOffset = 0u;
        compactShader.CompactMode = 1u;
        GL.DispatchCompute((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("FeedbackGather compact pass (duplicate patchIds)");

        Assert.Equal(1u, counter.Read());
        RequestGpu[] outReq = requests.ReadBack(count: 1);
        Assert.Equal(new RequestGpu(0u, 777u, 0u, 777u), outReq[0]);

        // Programs are disposed via ComputeProgram.
    }

    private static readonly RequestGpu Sentinel = new(0xFFFF_FFFFu, 0xFFFF_FFFFu, 0xFFFF_FFFFu, 0xFFFF_FFFFu);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RequestGpu(uint ChunkSlot, uint VirtualPageIndex, uint Mip, uint PatchId);

    private sealed class RequestGpuComparer : IEqualityComparer<RequestGpu>
    {
        public static readonly RequestGpuComparer Instance = new();

        public bool Equals(RequestGpu x, RequestGpu y)
            => x.ChunkSlot == y.ChunkSlot
                && x.VirtualPageIndex == y.VirtualPageIndex
                && x.Mip == y.Mip
                && x.PatchId == y.PatchId;

        public int GetHashCode(RequestGpu obj)
            => HashCode.Combine(obj.ChunkSlot, obj.VirtualPageIndex, obj.Mip, obj.PatchId);
    }

    private sealed class AtomicCounterBuffer : IDisposable
    {
        private int bufferId;

        public AtomicCounterBuffer(int bufferId) => this.bufferId = bufferId;

        public void BindBase(int bindingIndex)
        {
            GL.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, bufferId);
        }

        public uint Read()
        {
            uint value = 0u;
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, bufferId);
            GL.GetBufferSubData(BufferTarget.AtomicCounterBuffer, IntPtr.Zero, sizeof(uint), ref value);
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
            return value;
        }

        public void Upload(uint value)
        {
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, bufferId);
            GL.BufferSubData(BufferTarget.AtomicCounterBuffer, IntPtr.Zero, sizeof(uint), ref value);
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
        }

        public void Dispose()
        {
            int id = bufferId;
            bufferId = 0;
            if (id != 0) GL.DeleteBuffer(id);
        }
    }

    private static AtomicCounterBuffer CreateAtomicCounterBuffer(uint initialValue, int counterCount = 1)
    {
        if (counterCount <= 0) throw new ArgumentOutOfRangeException(nameof(counterCount));

        int id = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, id);
        uint[] init = new uint[counterCount];
        init[0] = initialValue;
        GL.BufferData(BufferTarget.AtomicCounterBuffer, checked(sizeof(uint) * counterCount), init, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
        return new AtomicCounterBuffer(id);
    }

    private sealed class Ssbo<T> : IDisposable where T : unmanaged
    {
        private readonly int capacityItems;
        private readonly GpuShaderStorageBuffer buffer;

        public Ssbo(GpuShaderStorageBuffer buffer, int capacityItems)
        {
            this.buffer = buffer;
            this.capacityItems = capacityItems;
        }

        public void BindBase(int bindingIndex) => buffer.BindBase(bindingIndex);

        public T[] ReadBack(int count)
        {
            count = Math.Clamp(count, 0, capacityItems);
            var dst = new T[count];
            using var mapped = buffer.MapRange<T>(dstOffsetBytes: 0, elementCount: count, access: MapBufferAccessMask.MapReadBit);
            if (!mapped.IsMapped)
            {
                return dst;
            }
            mapped.Span.CopyTo(dst);
            return dst;
        }

        public void Dispose() => buffer.Dispose();
    }

    private static Ssbo<T> CreateSsbo<T>(string debugName, int capacityItems, T sentinel) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicRead, debugName: debugName);
        int bytes = checked(capacityItems * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);

        T[] init = new T[capacityItems];
        for (int i = 0; i < init.Length; i++) init[i] = sentinel;
        ssbo.UploadSubData(init, dstOffsetBytes: 0, byteCount: bytes);

        return new Ssbo<T>(ssbo, capacityItems);
    }

    private static void BindSampler2DUint(int program, string uniformName, int textureId, int unit)
    {
        int loc = GL.GetUniformLocation(program, uniformName);
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        if (loc >= 0)
        {
            GL.Uniform1(loc, unit);
        }
    }

    private static void BindSampler2DArrayUint(int program, string uniformName, int textureId, int unit)
    {
        int loc = GL.GetUniformLocation(program, uniformName);
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        if (loc >= 0)
        {
            GL.Uniform1(loc, unit);
        }
    }

    private static void SetUniform(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0 && ComputeProgram.TryGetExplicitUniformLocation(program, name, out int explicitLoc))
        {
            loc = explicitLoc;
        }

        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static ShaderTestHelper CreateShaderHelperOrSkip()
    {
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.Skip("Shader assets not available - test output content may be missing");
        }

        return new ShaderTestHelper(shaderPath, includePath);
    }
}
