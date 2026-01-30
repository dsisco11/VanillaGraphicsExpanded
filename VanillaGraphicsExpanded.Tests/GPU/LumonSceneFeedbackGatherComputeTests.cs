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
    public void FeedbackGather_EmitsRequests_ForNonZeroPatchIds()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_feedback_gather.csh");

        const int w = 8;
        const int h = 8;

        using var patchId = Texture2D.Create(w, h, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

        // RGBA32UI per texel: (chunkSlot, patchId, packedUv, misc)
        uint[] pid = new uint[w * h * 4];
        void Set(int x, int y, uint chunkSlot, uint patch)
        {
            int idx = (y * w + x) * 4;
            pid[idx + 0] = chunkSlot;
            pid[idx + 1] = patch;
            pid[idx + 2] = 0u;
            pid[idx + 3] = 0u;
        }

        // patchId==0 should be ignored.
        Set(0, 0, chunkSlot: 0u, patch: 0u);

        // A few sparse requests (mix chunkSlot for contract validation; v1 runtime only uses slot 0).
        Set(1, 0, chunkSlot: 0u, patch: 1u);
        Set(2, 3, chunkSlot: 5u, patch: 777u);
        Set(7, 7, chunkSlot: 2u, patch: 65535u);

        patchId.UploadDataImmediate(pid);

        const uint capacity = 64;
        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)capacity, sentinel: Sentinel);
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        GL.UseProgram(program);

        // Match shader bindings:
        // - atomic counter binding=0
        // - SSBO binding=0
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);

        BindSampler2DUint(program, "vge_patchIdGBuffer", patchId.TextureId, unit: 0);
        SetUniform(program, "vge_maxRequests", capacity);

        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        uint requestCount = counter.Read();
        Assert.Equal(3u, requestCount);

        RequestGpu[] outReq = requests.ReadBack(count: (int)requestCount);

        // Ordering is not stable; verify as a multiset.
        var expected = new Dictionary<RequestGpu, int>(RequestGpuComparer.Instance)
        {
            [new RequestGpu(0u, 1u % 16384u, 0u, 1u)] = 1,
            [new RequestGpu(5u, 777u % 16384u, 0u, 777u)] = 1,
            [new RequestGpu(2u, 65535u % 16384u, 0u, 65535u)] = 1,
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

        GL.DeleteProgram(program);
    }

    [Fact]
    public void FeedbackGather_RespectsMaxRequests_AndDoesNotWritePastCapacity()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_feedback_gather.csh");

        const int w = 16;
        const int h = 16;

        using var patchId = Texture2D.Create(w, h, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

        // All pixels request the same patchId (duplicates are expected).
        uint[] pid = new uint[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            pid[i * 4 + 0] = 0u;     // chunkSlot
            pid[i * 4 + 1] = 777u;   // patchId
            pid[i * 4 + 2] = 0u;
            pid[i * 4 + 3] = 0u;
        }
        patchId.UploadDataImmediate(pid);

        // Allocate more SSBO entries than maxRequests so we can verify untouched entries remain sentinel.
        const uint ssboCapacity = 16;
        const uint maxRequests = 8;

        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)ssboCapacity, sentinel: Sentinel);
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        GL.UseProgram(program);
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);

        BindSampler2DUint(program, "vge_patchIdGBuffer", patchId.TextureId, unit: 0);
        SetUniform(program, "vge_maxRequests", maxRequests);

        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Counter increments for all valid pixels, even when idx >= maxRequests.
        Assert.Equal((uint)(w * h), counter.Read());

        RequestGpu[] outReq = requests.ReadBack(count: (int)ssboCapacity);

        // First maxRequests entries should be written; remaining must remain sentinel.
        for (int i = 0; i < (int)ssboCapacity; i++)
        {
            if (i < maxRequests)
            {
                Assert.Equal(0u, outReq[i].ChunkSlot);
                Assert.Equal(777u % 16384u, outReq[i].VirtualPageIndex);
                Assert.Equal(0u, outReq[i].Mip);
                Assert.Equal(777u, outReq[i].PatchId);
            }
            else
            {
                Assert.Equal(Sentinel, outReq[i]);
            }
        }

        GL.DeleteProgram(program);
    }

    [Fact]
    public void FeedbackGather_AllowsDuplicatePatchIds()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_feedback_gather.csh");

        const int w = 8;
        const int h = 8;

        using var patchId = Texture2D.Create(w, h, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

        uint[] pid = new uint[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            pid[i * 4 + 0] = 5u;     // chunkSlot
            pid[i * 4 + 1] = 42u;    // patchId
            pid[i * 4 + 2] = 0u;
            pid[i * 4 + 3] = 0u;
        }
        patchId.UploadDataImmediate(pid);

        const uint capacity = (uint)(w * h);
        using var requests = CreateSsbo<RequestGpu>("Test_PageRequests", capacityItems: (int)capacity, sentinel: Sentinel);
        using var counter = CreateAtomicCounterBuffer(initialValue: 0u);

        GL.UseProgram(program);
        counter.BindBase(bindingIndex: 0);
        requests.BindBase(bindingIndex: 0);

        BindSampler2DUint(program, "vge_patchIdGBuffer", patchId.TextureId, unit: 0);
        SetUniform(program, "vge_maxRequests", capacity);

        GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        Assert.Equal(capacity, counter.Read());

        RequestGpu[] outReq = requests.ReadBack(count: (int)capacity);
        for (int i = 0; i < outReq.Length; i++)
        {
            Assert.Equal(5u, outReq[i].ChunkSlot);
            Assert.Equal(42u % 16384u, outReq[i].VirtualPageIndex);
            Assert.Equal(0u, outReq[i].Mip);
            Assert.Equal(42u, outReq[i].PatchId);
        }

        GL.DeleteProgram(program);
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

        // Fill with sentinel.
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
