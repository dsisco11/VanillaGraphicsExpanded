using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.GPU.Shaders;

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
        using var markShader = new LumonSceneFeedbackMarkPagesShader(helper, debugName: "Tests.MultiSlotCaptureConvergence.Mark");
        using var compactShader = new LumonSceneFeedbackCompactPagesShader(helper, debugName: "Tests.MultiSlotCaptureConvergence.Compact");
        using var captureComputeProgram = ComputeProgram.Create(helper, "lumonscene_capture_voxel.csh", debugName: "Tests.MultiSlotCaptureConvergence.Capture");
        int captureProgram = captureComputeProgram.ProgramId;
        using var captureParamsUbo = new ObjectParamsUbo("Tests.MultiSlotCaptureConvergence.CaptureParams");

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
        var pageTableStats = new LumonScenePageTableStatsTracker();
        pageTableStats.Reset(newChunkSlotCount: chunkSlotCount);
        var cpuProc = new LumonSceneFeedbackRequestProcessor(pool, pageTableMirror, virtualToPhysical, physicalToVirtual, new NoopPageTableWriter(), pageTableStats);

        // GPU atlas outputs.
        using var depthAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, atlasW, atlasH, atlasCount, value: 0f);
        FillRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount, r: 0, g: 0, b: 0, a: 0);
        const int occRes = 32;
        using var occL0 = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        using var materialPalette = Texture2D.Create(width: 64, height: 1, format: PixelInternalFormat.Rgba32ui, filter: TextureFilterMode.Nearest, debugName: "Test_MaterialPalette");

        uint occPacked = LumonSceneOccupancyPacking.Pack(blockLevel: 0, sunLevel: 0, lightId: 0, materialPaletteIndex: 1);
        uint[] occ = new uint[occRes * occRes * occRes];
        Array.Fill(occ, occPacked);
        occL0.UploadDataImmediate(occ, x: 0, y: 0, z: 0, regionWidth: occRes, regionHeight: occRes, regionDepth: occRes, mipLevel: 0);

        uint[] pal = new uint[64 * 4];
        uint sid = 9u;
        uint packed2 = sid | (sid << 16);
        pal[1 * 4 + 0] = packed2;
        pal[1 * 4 + 1] = packed2;
        pal[1 * 4 + 2] = packed2;
        pal[1 * 4 + 3] = 0u;
        materialPalette.UploadDataImmediate(pal);

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
        using var markCounters = CreateAtomicCounterBuffer(counterCount: 3);

        var captureOut = new LumonSceneCaptureWorkGpu[maxRequestsPerFrame];
        var relightOut = new LumonSceneRelightWorkGpu[maxRequestsPerFrame];
        int recaptureCursor = 0;

        // Run enough frames to fully allocate + capture all pages.
        const int maxFrames = 96;
        uint scanOffset = 0u;

        for (uint frameStamp = 1u; frameStamp <= maxFrames && virtualToPhysical.Count < totalPages; frameStamp++)
        {
            // Pass A: mark.
            markCounters.BindBase(bindingIndex: 0);
            markShader.Use();
            markShader.BindPatchIdGBuffer(patchIdGBuffer.TextureId);
            markShader.BindChunkSlotGenerationTex(genTex.TextureId);
            markShader.FrameStamp = frameStamp;
            markShader.BindPageUsageStampImage(usageStamp.TextureId, access: TextureAccess.ReadWrite);
            GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            GpuTestFence.WaitForGpuOrSkip("MultiSlotCapture mark pass dispatch");

            // Pass B: compact.
            pageRequestCounter.UploadZeros(counterCount: 1);
            pageRequestCounter.BindBase(bindingIndex: 0);
            pageRequests.BindBase(bindingIndex: 0);
            compactShader.Use();
            compactShader.BindPageUsageStamp(usageStamp.TextureId);
            compactShader.BindPageTableMip0(pageTableMip0.TextureId);
            compactShader.MaxRequests = (uint)maxRequestsPerFrame;
            compactShader.FrameStamp = frameStamp;
            compactShader.ScanOffset = scanOffset;
            compactShader.CompactMode = 1u;
            GL.DispatchCompute((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            GpuTestFence.WaitForGpuOrSkip("MultiSlotCapture compact pass dispatch");

            scanOffset = (scanOffset + (uint)LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk) % (uint)(LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount);

            uint requestCount = pageRequestCounter.Read(counterIndex: 0);
            int toRead = Math.Min((int)requestCount, maxRequestsPerFrame);
            LumonScenePageRequestGpu[] requests = ReadSsbo<LumonScenePageRequestGpu>(pageRequests, itemCount: toRead);

            cpuProc.Process(
                requests: requests,
                maxRequestsToProcess: toRead,
                maxNewAllocations: maxNewAllocsPerFrame,
                maxResidentPagesPerChunkSlot: 0,
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

            BindSampler3D(unit: 2, occL0.TextureId);
            BindSampler2D(unit: 3, materialPalette.TextureId);

            LumonSceneCaptureVoxelParamsUbo.Bind(
                captureParamsUbo,
                tileSizeTexels: (uint)tileSize,
                tilesPerAxis: (uint)tilesPerAxis,
                tilesPerAtlas: (uint)tilesPerAtlas,
                borderTexels: 0u,
                occOriginMinCell0X: 0,
                occOriginMinCell0Y: 0,
                occOriginMinCell0Z: 0,
                occRing0X: 0,
                occRing0Y: 0,
                occRing0Z: 0,
                occResolution: occRes);

            int gx = (tileSize + 7) / 8;
            int gy = (tileSize + 7) / 8;
            GL.DispatchCompute(gx, gy, captureCount);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit);

            GpuTestFence.WaitForGpuOrSkip("MultiSlotCapture capture pass dispatch");
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

        // Validate: every allocated physical page has non-zero oct-normal in its tile.
        byte[] rgba = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount);
        foreach (uint physicalPageId in virtualToPhysical.Values)
        {
            pool.PagePool.DecodePhysicalId(physicalPageId, out ushort atlasIdx, out ushort tileX, out ushort tileY);
            int texelX = tileX * tileSize;
            int texelY = tileY * tileSize;
            int idx = (((atlasIdx * atlasH) + texelY) * atlasW + texelX) * 4;
            byte r = rgba[idx + 0];
            byte g = rgba[idx + 1];
            Assert.True((r | g) != 0, $"Expected tile for physicalPageId={physicalPageId} to be written (oct-normal!=0).");
        }

        // Programs are disposed via ComputeProgram.
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
        // Compute shaders use layout(binding=...) for sampler units; SPIR-V uniform names may not be reflectable.
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private static void BindSampler2DArrayUint(int program, string uniformName, int textureId, int unit)
    {
        // Compute shaders use layout(binding=...) for sampler units; SPIR-V uniform names may not be reflectable.
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private static void SetUniform1ui(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0 && ComputeProgram.TryGetExplicitUniformLocation(program, name, out int explicitLoc))
        {
            loc = explicitLoc;
        }

        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static void SetUniform1i(int program, string name, int value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0 && ComputeProgram.TryGetExplicitUniformLocation(program, name, out int explicitLoc))
        {
            loc = explicitLoc;
        }

        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static void SetUniform3i(int program, string name, int x, int y, int z)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0 && ComputeProgram.TryGetExplicitUniformLocation(program, name, out int explicitLoc))
        {
            loc = explicitLoc;
        }

        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform3(loc, x, y, z);
    }

    private static void BindSampler3D(int unit, int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture3D, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private static void BindSampler2D(int unit, int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
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
