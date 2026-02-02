using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonSceneMultiSlotMaterialCaptureConvergenceTests : RenderTestBase
{
    public LumonSceneMultiSlotMaterialCaptureConvergenceTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void MaterialAtlas_MultiFrame_100ChunkSlots_EventuallyPopulatesAllTiles()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int markProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_mark_pages.csh");
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");
        int captureProgram = CompileAndLinkCompute(helper, "lumonscene_capture_voxel.csh");

        const int chunkSlotCount = 100;
        const int pagesPerChunk = 4;
        const int totalPages = chunkSlotCount * pagesPerChunk;

        // PatchIdGBuffer: request the same 4 patchIds in every chunkSlot.
        const int gW = 256;
        const int gH = 2;
        Assert.True(gW * gH >= totalPages);

        using var patchIdGBuffer = Texture2D.Create(gW, gH, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");
        uint[] pidTexels = new uint[gW * gH * 4];
        for (int s = 0; s < chunkSlotCount; s++)
        {
            for (int p = 0; p < pagesPerChunk; p++)
            {
                int pixel = s * pagesPerChunk + p;
                int idx = pixel * 4;
                pidTexels[idx + 0] = (uint)s;         // chunkSlot
                pidTexels[idx + 1] = (uint)(p + 1);   // patchId => virtualPageIndex = patchId % 16384
                pidTexels[idx + 2] = 0u;              // packed uv unused in v2 gather
                pidTexels[idx + 3] = 0u;              // generation16
            }
        }
        patchIdGBuffer.UploadDataImmediate(pidTexels);

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

        // Physical pool (CPU-only; single atlas large enough).
        const int tileSize = 8;
        const int tilesPerAxis = 32; // 1024 pages
        const int tilesPerAtlas = tilesPerAxis * tilesPerAxis;
        const int atlasCount = 1;
        const int atlasW = tileSize * tilesPerAxis;
        const int atlasH = tileSize * tilesPerAxis;

        var pool = new LumonScenePhysicalFieldPool(LumonSceneField.Near);
        pool.Configure(new LumonScenePhysicalPoolPlan(
            field: LumonSceneField.Near,
            tileSizeTexels: tileSize,
            tilesPerAxis: tilesPerAxis,
            tilesPerAtlas: tilesPerAtlas,
            requestedPages: totalPages,
            capacityPages: totalPages,
            atlasCount: atlasCount,
            isClampedByMaxAtlases: false));

        var pageTableMirror = new LumonScenePageTableEntry[chunkSlotCount * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<ulong, uint>(capacity: totalPages);
        var physicalToVirtual = new Dictionary<uint, ulong>(capacity: totalPages);
        var cpuProc = new LumonSceneFeedbackRequestProcessor(pool, pageTableMirror, virtualToPhysical, physicalToVirtual, new NoopPageTableWriter());

        // GPU atlas outputs.
        using var depthAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, atlasW, atlasH, atlasCount, value: 0f);
        FillRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount, r: 0, g: 0, b: 0, a: 0);

        // Patch metadata (indexed by physicalPageId).
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[totalPages + 1]);

        // ChunkSlot info (one ivec4 per slot): origin blocks + generation.
        int[] slotInfo = new int[chunkSlotCount * 4];
        for (int s = 0; s < chunkSlotCount; s++)
        {
            int gx = s % 10;
            int gz = s / 10;
            slotInfo[s * 4 + 0] = gx * 32;
            slotInfo[s * 4 + 1] = 0;
            slotInfo[s * 4 + 2] = gz * 32;
            slotInfo[s * 4 + 3] = 0; // generation
        }
        using var slotInfoSsbo = CreateSsbo<int>("Test_ChunkSlotInfoSSBO", slotInfo);

        // Feedback outputs (bounded request list).
        const int maxRequestsPerFrame = 64;
        const int maxNewAllocsPerFrame = 16;
        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: maxRequestsPerFrame);
        using var pageRequestCounter = CreateAtomicCounterBuffer(counterCount: 1);

        var captureOut = new LumonSceneCaptureWorkGpu[maxRequestsPerFrame];
        var relightOut = new LumonSceneRelightWorkGpu[maxRequestsPerFrame];
        int recaptureCursor = 0;

        // Run enough frames to fully allocate + capture all pages.
        const int maxFrames = 96;
        uint scanOffset = 0u;

        for (uint frameStamp = 1u; frameStamp <= maxFrames && virtualToPhysical.Count < totalPages; frameStamp++)
        {
            // Pass A: mark.
            GL.UseProgram(markProgram);
            BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
            BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
            SetUniform1ui(markProgram, "vge_frameStamp", frameStamp);
            GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
            GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            // Pass B: compact.
            pageRequestCounter.UploadZeros(counterCount: 1);
            GL.UseProgram(compactProgram);
            pageRequestCounter.BindBase(bindingIndex: 0);
            pageRequests.BindBase(bindingIndex: 0);
            GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadOnly, format: SizedInternalFormat.R32ui);
            SetUniform1ui(compactProgram, "vge_maxRequests", (uint)maxRequestsPerFrame);
            SetUniform1ui(compactProgram, "vge_frameStamp", frameStamp);
            SetUniform1ui(compactProgram, "vge_scanOffset", scanOffset);
            GL.DispatchCompute((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            scanOffset = (scanOffset + (uint)LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk) % (uint)(LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount);

            uint requestCount = pageRequestCounter.Read(counterIndex: 0);
            int toRead = Math.Min((int)requestCount, maxRequestsPerFrame);
            LumonScenePageRequestGpu[] requests = ReadSsbo<LumonScenePageRequestGpu>(pageRequests, itemCount: toRead);

            cpuProc.Process(
                requests: requests,
                maxRequestsToProcess: toRead,
                maxNewAllocations: maxNewAllocsPerFrame,
                maxPagesPerChunkSlot: 1024,
                recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
                recaptureCursor: ref recaptureCursor,
                maxRecapture: 0,
                captureWorkOut: captureOut,
                relightWorkOut: relightOut,
                captureCount: out int captureCount,
                relightCount: out _,
                stats: out _);

            if (captureCount <= 0)
            {
                continue;
            }

            using var captureSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_CaptureWorkSSBO", (ReadOnlySpan<LumonSceneCaptureWorkGpu>)captureOut.AsSpan(0, captureCount));

            GL.UseProgram(captureProgram);
            captureSsbo.BindBase(bindingIndex: 0);
            patchMetaSsbo.BindBase(bindingIndex: 1);
            slotInfoSsbo.BindBase(bindingIndex: 2);

            GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
            GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

            SetUniform1ui(captureProgram, "vge_tileSizeTexels", (uint)tileSize);
            SetUniform1ui(captureProgram, "vge_tilesPerAxis", (uint)tilesPerAxis);
            SetUniform1ui(captureProgram, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
            SetUniform1ui(captureProgram, "vge_borderTexels", 0u);

            int gx = (tileSize + 7) / 8;
            int gy = (tileSize + 7) / 8;
            GL.DispatchCompute(gx, gy, captureCount);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit);
        }

        Assert.Equal(totalPages, virtualToPhysical.Count);

        // Validate per-slot coverage.
        int[] perSlot = new int[chunkSlotCount];
        foreach (ulong key in virtualToPhysical.Keys)
        {
            int slot = (int)LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
            if ((uint)slot < (uint)perSlot.Length) perSlot[slot]++;
        }
        for (int s = 0; s < perSlot.Length; s++)
        {
            Assert.Equal(pagesPerChunk, perSlot[s]);
        }

        // Validate: every allocated physical page has alpha!=0 in its tile.
        byte[] rgba = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount);
        foreach (uint physicalPageId in virtualToPhysical.Values)
        {
            pool.PagePool.DecodePhysicalId(physicalPageId, out ushort atlasIdx, out ushort tileX, out ushort tileY);
            int texelX = tileX * tileSize;
            int texelY = tileY * tileSize;
            int idx = (((atlasIdx * atlasH) + texelY) * atlasW + texelX) * 4;
            byte a = rgba[idx + 3];
            Assert.True(a != 0, $"Expected tile for physicalPageId={physicalPageId} to be written (alpha!=0).");
        }

        GL.DeleteProgram(markProgram);
        GL.DeleteProgram(compactProgram);
        GL.DeleteProgram(captureProgram);
    }

    private sealed class NoopPageTableWriter : ILumonScenePageTableWriter
    {
        public void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry) { }
    }

    private sealed class AtomicCounterBuffer : IDisposable
    {
        private int bufferId;

        public AtomicCounterBuffer(int bufferId) => this.bufferId = bufferId;

        public void BindBase(int bindingIndex)
            => GL.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, bufferId);

        public uint Read(int counterIndex)
        {
            uint value = 0u;
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, bufferId);
            GL.GetBufferSubData(BufferTarget.AtomicCounterBuffer, (IntPtr)(counterIndex * sizeof(uint)), sizeof(uint), ref value);
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
            return value;
        }

        public void UploadZeros(int counterCount)
        {
            counterCount = Math.Max(1, counterCount);
            uint[] zeros = new uint[counterCount];
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, bufferId);
            GL.BufferSubData(BufferTarget.AtomicCounterBuffer, IntPtr.Zero, sizeof(uint) * counterCount, zeros);
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
        }

        public void Dispose()
        {
            int id = bufferId;
            bufferId = 0;
            if (id != 0) GL.DeleteBuffer(id);
        }
    }

    private static AtomicCounterBuffer CreateAtomicCounterBuffer(int counterCount)
    {
        counterCount = Math.Max(1, counterCount);
        int id = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, id);
        uint[] zeros = new uint[counterCount];
        GL.BufferData(BufferTarget.AtomicCounterBuffer, sizeof(uint) * counterCount, zeros, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
        return new AtomicCounterBuffer(id);
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, int capacityItems) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        ssbo.Allocate(checked(capacityItems * Unsafe.SizeOf<T>()));
        return ssbo;
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, T[] data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        ssbo.Allocate(checked(data.Length * Unsafe.SizeOf<T>()));
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: checked(data.Length * Unsafe.SizeOf<T>()));
        return ssbo;
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        ssbo.Allocate(checked(data.Length * Unsafe.SizeOf<T>()));
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: checked(data.Length * Unsafe.SizeOf<T>()));
        return ssbo;
    }

    private static T[] ReadSsbo<T>(GpuShaderStorageBuffer ssbo, int itemCount) where T : unmanaged
    {
        T[] dst = new T[itemCount];
        using var mapped = ssbo.MapRange<T>(dstOffsetBytes: 0, elementCount: itemCount, access: MapBufferAccessMask.MapReadBit);
        Assert.True(mapped.IsMapped);
        mapped.Span.CopyTo(dst);
        return dst;
    }

    private static void BindSampler2DUint(int program, string uniformName, int textureId, int unit)
    {
        int loc = GL.GetUniformLocation(program, uniformName);
        Assert.True(loc >= 0, $"Missing uniform {uniformName}");
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.Uniform1(loc, unit);
    }

    private static void SetUniform1ui(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static void FillR16f2DArray(int textureId, int width, int height, int depth, float value)
    {
        float[] data = new float[checked(width * height * depth)];
        Array.Fill(data, value);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Red, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static void FillRgba8_2DArray(int textureId, int width, int height, int depth, byte r, byte g, byte b, byte a)
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
