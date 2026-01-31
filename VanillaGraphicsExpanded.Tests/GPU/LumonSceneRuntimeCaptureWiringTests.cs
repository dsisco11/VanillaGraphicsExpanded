using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonSceneRuntimeCaptureWiringTests : RenderTestBase
{
    public LumonSceneRuntimeCaptureWiringTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void RuntimeWiring_FeedbackToCpuToCapture_WritesExpectedMaterialNormals()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int markProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_mark_pages.csh");
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");
        int captureProgram = CompileAndLinkCompute(helper, "lumonscene_capture_voxel.csh");

        // Use patchIds 1..6 which correspond to +/-X,+/-Y,+/-Z normals in the capture shader.
        // Keep patchId == virtualPageIndex for v1/v2, so the compacted request's Flags field still works as patchId.
        uint[] patchIds = [1u, 2u, 3u, 4u, 5u, 6u];
        const int desiredPages = 6;

        // PatchIdGBuffer: place 6 non-zero pixels.
        const int gW = 32;
        const int gH = 32;
        using var patchIdGBuffer = Texture2D.Create(gW, gH, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

        uint[] pidTexels = new uint[gW * gH * 4];
        for (int i = 0; i < patchIds.Length; i++)
        {
            int x = i % gW;
            int y = i / gW;
            int idx = (y * gW + x) * 4;
            pidTexels[idx + 0] = 0u;         // chunkSlot
            pidTexels[idx + 1] = patchIds[i]; // patchId
            pidTexels[idx + 2] = 0u;
            pidTexels[idx + 3] = 0u;
        }
        patchIdGBuffer.UploadDataImmediate(pidTexels);

        // Feedback dedup stamp + bounded request list.
        using var usageStamp = Texture2D.Create(128, 128, PixelInternalFormat.R32ui, debugName: "Test_PageUsageStamp");
        usageStamp.UploadDataImmediate(new uint[128 * 128]);

        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: desiredPages);
        using var pageRequestCounter = CreateAtomicCounterBuffer(initialValue: 0u);

        // Pass A: mark.
        GL.UseProgram(markProgram);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
        SetUniform(markProgram, "vge_frameStamp", 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: false, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Pass B: compact.
        GL.UseProgram(compactProgram);
        pageRequestCounter.BindBase(bindingIndex: 0);
        pageRequests.BindBase(bindingIndex: 0);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: false, layer: 0, access: TextureAccess.ReadOnly, format: SizedInternalFormat.R32ui);
        SetUniform(compactProgram, "vge_maxRequests", (uint)desiredPages);
        SetUniform(compactProgram, "vge_frameStamp", 1u);
        GL.DispatchCompute((16384 + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        uint writtenRequests = pageRequestCounter.Read();
        Assert.Equal((uint)desiredPages, writtenRequests);

        LumonScenePageRequestGpu[] requests = pageRequests.ReadBack(count: desiredPages);
        Array.Sort(requests, static (a, b) => a.VirtualPageIndex.CompareTo(b.VirtualPageIndex));

        // CPU request processing -> capture work.
        const int tileSize = 16;
        const int tilesPerAxis = 4;                 // atlas dims 64x64
        const int tilesPerAtlas = tilesPerAxis * tilesPerAxis; // 16
        const int atlasCount = 1;
        const int capacityPages = tilesPerAtlas * atlasCount;

        var pool = new LumonScenePhysicalFieldPool(LumonSceneField.Near);
        pool.Configure(new LumonScenePhysicalPoolPlan(
            field: LumonSceneField.Near,
            tileSizeTexels: tileSize,
            tilesPerAxis: tilesPerAxis,
            tilesPerAtlas: tilesPerAtlas,
            requestedPages: desiredPages,
            capacityPages: capacityPages,
            atlasCount: atlasCount,
            isClampedByMaxAtlases: false));

        var pageTableMirror = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<int, uint>();
        var physicalToVirtual = new Dictionary<uint, int>();
        var cpuProc = new LumonSceneFeedbackRequestProcessor(pool, pageTableMirror, virtualToPhysical, physicalToVirtual, new NullPageTableWriter());

        var captureOut = new LumonSceneCaptureWorkGpu[desiredPages];
        var relightOut = new LumonSceneRelightWorkGpu[desiredPages];
        int recaptureCursor = 0;

        cpuProc.Process(
            requests: requests,
            maxRequestsToProcess: desiredPages,
            maxNewAllocations: desiredPages,
            recaptureVirtualPages: ReadOnlySpan<int>.Empty,
            recaptureCursor: ref recaptureCursor,
            maxRecapture: 0,
            captureWorkOut: captureOut,
            relightWorkOut: relightOut,
            captureCount: out int captureCount,
            relightCount: out _,
            stats: out _);

        Assert.Equal(desiredPages, captureCount);
        Assert.Equal(desiredPages, virtualToPhysical.Count);

        // Capture -> material atlas.
        int atlasW = tileSize * tilesPerAxis;
        int atlasH = tileSize * tilesPerAxis;

        using var depthAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");

        ClearR16f2DArray(depthAtlas.TextureId, atlasW, atlasH, atlasCount, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount, r: 0, g: 0, b: 0, a: 0);

        using var captureWorkSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_CaptureWorkSSBO", captureOut.AsSpan(0, captureCount));

        GL.UseProgram(captureProgram);
        captureWorkSsbo.BindBase(bindingIndex: 0);
        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        SetUniform(captureProgram, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(captureProgram, "vge_tilesPerAxis", (uint)tilesPerAxis);
        SetUniform(captureProgram, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
        _ = TrySetUniform(captureProgram, "vge_borderTexels", 0u);

        int gxCap = (tileSize + 7) / 8;
        int gyCap = (tileSize + 7) / 8;
        GL.DispatchCompute(gxCap, gyCap, captureCount);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Validate: each captured tile center has alpha=255 and the expected normal for its patchId.
        byte[] mat = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount);

        for (int i = 0; i < captureCount; i++)
        {
            uint physicalPageId = captureOut[i].PhysicalPageId;
            uint patchId = captureOut[i].PatchId;

            pool.PagePool.DecodePhysicalId(physicalPageId, out ushort atlasIdx, out ushort tileX, out ushort tileY);
            int cx = tileX * tileSize + tileSize / 2;
            int cy = tileY * tileSize + tileSize / 2;

            (Vector3 n, byte a) = ReadNormalAndAlphaAt(mat, atlasW, atlasH, layer: atlasIdx, x: cx, y: cy);
            Assert.Equal((byte)255, a);

            Vector3 expected = ExpectedNormalFromPatchId(patchId);
            float dot = Vector3.Dot(Vector3.Normalize(n), expected);
            Assert.True(dot > 0.99f, $"Expected normal {expected} for patchId={patchId}, got {n} (dot={dot}).");
        }

        GL.DeleteProgram(markProgram);
        GL.DeleteProgram(compactProgram);
        GL.DeleteProgram(captureProgram);
    }

    private sealed class NullPageTableWriter : ILumonScenePageTableWriter
    {
        public void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry) { }
    }

    private sealed class AtomicCounterBuffer : IDisposable
    {
        private int bufferId;

        public AtomicCounterBuffer(int bufferId) => this.bufferId = bufferId;

        public void BindBase(int bindingIndex)
            => GL.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, bufferId);

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

    private static Ssbo<T> CreateSsbo<T>(string debugName, int capacityItems) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicRead, debugName: debugName);
        int bytes = checked(capacityItems * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        return new Ssbo<T>(ssbo, capacityItems);
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
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

    private static bool TrySetUniform(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0) return false;
        GL.Uniform1(loc, value);
        return true;
    }

    private static void ClearR16f2DArray(int textureId, int width, int height, int depth, float value)
    {
        float[] data = new float[checked(width * height * depth)];
        Array.Fill(data, value);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Red, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static void ClearRgba8_2DArray(int textureId, int width, int height, int depth, byte r, byte g, byte b, byte a)
    {
        byte[] data = new byte[checked(width * height * depth * 4)];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i + 0] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = a;
        }
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static byte[] ReadTexImageRgba8_2DArray(int textureId, int width, int height, int depth)
    {
        byte[] data = new byte[checked(width * height * depth * 4)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.GetTexImage(TextureTarget.Texture2DArray, level: 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
        return data;
    }

    private static (Vector3 Normal, byte A) ReadNormalAndAlphaAt(byte[] rgba, int width, int height, int layer, int x, int y)
    {
        int idx = (((layer * height) + y) * width + x) * 4;
        float nx = (rgba[idx + 0] / 255f) * 2f - 1f;
        float ny = (rgba[idx + 1] / 255f) * 2f - 1f;
        float nz = (rgba[idx + 2] / 255f) * 2f - 1f;
        return (new Vector3(nx, ny, nz), rgba[idx + 3]);
    }

    private static Vector3 ExpectedNormalFromPatchId(uint patchId)
    {
        if (patchId == 0u) return Vector3.UnitZ;
        uint f = (patchId - 1u) % 6u;
        return f switch
        {
            0u => Vector3.UnitX,
            1u => -Vector3.UnitX,
            2u => Vector3.UnitY,
            3u => -Vector3.UnitY,
            4u => Vector3.UnitZ,
            _ => -Vector3.UnitZ,
        };
    }
}

