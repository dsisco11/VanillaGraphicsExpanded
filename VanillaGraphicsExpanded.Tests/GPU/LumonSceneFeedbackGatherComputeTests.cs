using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

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
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");

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

        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)maxRequests, sentinel: Sentinel);
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        var seen = new bool[chunkSlotCount];

        GL.UseProgram(compactProgram);
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadOnly, format: SizedInternalFormat.R32ui);

        // Verify scanOffset rotation: offset increments by VirtualPagesPerChunk each "frame"
        // and should rotate the dominant slot for the bounded request list.
        const uint virtualPagesPerChunk = 128u * 128u;
        for (uint f = 0; f < (uint)chunkSlotCount; f++)
        {
            counter.Upload(value: 0u);

            SetUniform(compactProgram, "vge_maxRequests", maxRequests);
            SetUniform(compactProgram, "vge_frameStamp", frameStamp);
            SetUniform(compactProgram, "vge_scanOffset", f * virtualPagesPerChunk);

            // Dispatch only a single workgroup so the outcome is deterministic (no cross-workgroup atomic ordering).
            // This validates the scanOffset mapping logic itself (slot rotation), which is the core fairness mechanism.
            GL.DispatchCompute(1, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

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

        GL.DeleteProgram(compactProgram);
    }

    [Fact]
    public void FeedbackGather_DeduplicatesViaStampTexture_AndEmitsUniqueRequests()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int markProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_mark_pages.csh");
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");

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
        GL.UseProgram(markProgram);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchId.TextureId, unit: 0);
        BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
        SetUniform(markProgram, "vge_frameStamp", 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Pass B: compact stamps -> unique requests list.
        GL.UseProgram(compactProgram);
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadOnly, format: SizedInternalFormat.R32ui);
        SetUniform(compactProgram, "vge_maxRequests", capacity);
        SetUniform(compactProgram, "vge_frameStamp", 1u);
        SetUniform(compactProgram, "vge_scanOffset", 0u);
        GL.DispatchCompute((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

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

        GL.DeleteProgram(markProgram);
        GL.DeleteProgram(compactProgram);
    }

    [Fact]
    public void FeedbackGather_GenerationMismatch_IsRejectedInMarkPass()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int markProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_mark_pages.csh");
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");

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
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        GL.UseProgram(markProgram);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchId.TextureId, unit: 0);
        BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
        SetUniform(markProgram, "vge_frameStamp", 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GL.UseProgram(compactProgram);
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadOnly, format: SizedInternalFormat.R32ui);
        SetUniform(compactProgram, "vge_maxRequests", capacity);
        SetUniform(compactProgram, "vge_frameStamp", 1u);
        SetUniform(compactProgram, "vge_scanOffset", 0u);
        GL.DispatchCompute((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        uint requestCount = counter.Read();
        Assert.Equal(1u, requestCount);

        RequestGpu[] outReq = requests.ReadBack(count: (int)requestCount);
        Assert.Equal(new RequestGpu(0u, 1u, 0u, 1u), outReq[0]);

        GL.DeleteProgram(markProgram);
        GL.DeleteProgram(compactProgram);
    }

    [Fact]
    public void FeedbackGather_DeduplicatesDuplicatePatchIds()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int markProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_mark_pages.csh");
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");

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
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        GL.UseProgram(markProgram);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchId.TextureId, unit: 0);
        BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
        SetUniform(markProgram, "vge_frameStamp", 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GL.UseProgram(compactProgram);
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadOnly, format: SizedInternalFormat.R32ui);
        SetUniform(compactProgram, "vge_maxRequests", capacity);
        SetUniform(compactProgram, "vge_frameStamp", 1u);
        SetUniform(compactProgram, "vge_scanOffset", 0u);
        GL.DispatchCompute((16384 * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        Assert.Equal(1u, counter.Read());
        RequestGpu[] outReq = requests.ReadBack(count: 1);
        Assert.Equal(new RequestGpu(0u, 777u, 0u, 777u), outReq[0]);

        GL.DeleteProgram(markProgram);
        GL.DeleteProgram(compactProgram);
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

    private static AtomicCounterBuffer CreateAtomicCounterBuffer(uint initialValue)
    {
        int id = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, id);
        GL.BufferData(BufferTarget.AtomicCounterBuffer, sizeof(uint), ref initialValue, BufferUsageHint.DynamicDraw);
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
        Assert.True(loc >= 0, $"Missing uniform {uniformName}");
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.Uniform1(loc, unit);
    }

    private static void SetUniform(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static int CompileAndLinkCompute(ShaderTestHelper helper, string computeShaderFile)
    {
        var cs = helper.CompileShader(computeShaderFile, ShaderType.ComputeShader);
        Assert.True(cs.IsSuccess, cs.ErrorMessage);

        int program = GL.CreateProgram();
        GL.AttachShader(program, cs.ShaderId);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
        string log = GL.GetProgramInfoLog(program) ?? string.Empty;
        Assert.True(ok != 0, $"Compute program link failed:\n{log}");

        return program;
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
