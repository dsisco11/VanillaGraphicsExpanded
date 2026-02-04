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
public sealed class LumonSceneMaterialAtlasMultiFramePopulationTests : RenderTestBase
{
    public LumonSceneMaterialAtlasMultiFramePopulationTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void MaterialAtlas_MultiFrame_SingleChunk_EventuallyPopulatesAllAllocatedTiles()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int markProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_mark_pages.csh");
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");
        int captureProgram = CompileAndLinkCompute(helper, "lumonscene_capture_voxel.csh");

        const int tileSize = 8;
        const int tilesPerAxis = 16; // 16x16 = 256 pages
        const int tilesPerAtlas = tilesPerAxis * tilesPerAxis;
        const int atlasCount = 1;
        const int desiredPages = tilesPerAtlas;
        const int atlasW = tileSize * tilesPerAxis;
        const int atlasH = tileSize * tilesPerAxis;

        const int maxNewAllocsPerFrame = 8;
        const int maxFrames = 64;

        // CPU pool + mappings (single chunkSlot for now).
        var pool = new LumonScenePhysicalFieldPool(LumonSceneField.Near);
        pool.Configure(new LumonScenePhysicalPoolPlan(
            field: LumonSceneField.Near,
            tileSizeTexels: tileSize,
            tilesPerAxis: tilesPerAxis,
            tilesPerAtlas: tilesPerAtlas,
            requestedPages: desiredPages,
            capacityPages: desiredPages,
            atlasCount: atlasCount,
            isClampedByMaxAtlases: false));

        var pageTableMirror = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<ulong, uint>(capacity: desiredPages);
        var physicalToVirtual = new Dictionary<uint, ulong>(capacity: desiredPages);
        var pageTableStats = new LumonScenePageTableStatsTracker();
        pageTableStats.Reset(newChunkSlotCount: 1);
        var cpuProc = new LumonSceneFeedbackRequestProcessor(pool, pageTableMirror, virtualToPhysical, physicalToVirtual, new NoopPageTableWriter(), pageTableStats);

        // PatchIdGBuffer: request 256 unique pages (patchId==virtualPageIndex in [1..256]).
        const int gW = 64;
        const int gH = 64;
        Assert.True(gW * gH >= desiredPages);

        using var patchIdGBuffer = Texture2D.Create(gW, gH, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

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

        uint[] pidTexels = new uint[gW * gH * 4];
        for (int i = 0; i < desiredPages; i++)
        {
            int idx = i * 4;
            pidTexels[idx + 0] = 0u;           // chunkSlot (v1 only supports 0)
            pidTexels[idx + 1] = (uint)(i + 1); // patchId
        }
        patchIdGBuffer.UploadDataImmediate(pidTexels);

        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: desiredPages);
        using var pageRequestCounter = CreateAtomicCounterBuffer(counterCount: 1);

        // GPU atlas outputs (we only validate that tiles are written).
        using var depthAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        const int occRes = 32;
        using var occL0 = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        using var materialPalette = Texture2D.Create(width: 64, height: 1, format: PixelInternalFormat.Rgba32ui, filter: TextureFilterMode.Nearest, debugName: "Test_MaterialPalette");

        FillR16f2DArray(depthAtlas.TextureId, atlasW, atlasH, atlasCount, value: 0f);
        FillRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount, r: 0, g: 0, b: 0, a: 0);

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

        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[desiredPages + 1]);
        using var slotInfoSsbo = CreateSsbo<int>("Test_ChunkSlotInfoSSBO", new int[4]);

        var captureOut = new LumonSceneCaptureWorkGpu[desiredPages];
        var relightOut = new LumonSceneRelightWorkGpu[desiredPages];
        int recaptureCursor = 0;

        for (uint frameStamp = 1; frameStamp <= maxFrames && virtualToPhysical.Count < desiredPages; frameStamp++)
        {
            // Pass A: mark pages.
            GL.UseProgram(markProgram);
            BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
            BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
            SetUniform1ui(markProgram, "vge_frameStamp", frameStamp);
            GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
            GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            ResetAtomicCounter(pageRequestCounter, counterIndex: 0);

            // Pass B: compact stamps -> bounded request list.
            GL.UseProgram(compactProgram);
            pageRequestCounter.BindBase(bindingIndex: 0);
            pageRequests.BindBase(bindingIndex: 0);
            GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadOnly, format: SizedInternalFormat.R32ui);
            SetUniform1ui(compactProgram, "vge_maxRequests", (uint)desiredPages);
            SetUniform1ui(compactProgram, "vge_frameStamp", frameStamp);
            SetUniform1ui(compactProgram, "vge_scanOffset", 0u);
            GL.DispatchCompute((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
            Assert.Equal((uint)desiredPages, requestCount);

            LumonScenePageRequestGpu[] requests = ReadSsbo<LumonScenePageRequestGpu>(pageRequests, itemCount: desiredPages);
            Array.Sort(requests, static (a, b) => a.VirtualPageIndex.CompareTo(b.VirtualPageIndex));

            cpuProc.Process(
                requests: requests,
                maxRequestsToProcess: desiredPages,
                maxNewAllocations: maxNewAllocsPerFrame,
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

            SetUniform1ui(captureProgram, "vge_tileSizeTexels", (uint)tileSize);
            SetUniform1ui(captureProgram, "vge_tilesPerAxis", (uint)tilesPerAxis);
            SetUniform1ui(captureProgram, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
            SetUniform1ui(captureProgram, "vge_borderTexels", 0u);
            SetUniform3i(captureProgram, "vge_occOriginMinCell0", 0, 0, 0);
            SetUniform3i(captureProgram, "vge_occRing0", 0, 0, 0);
            SetUniform1i(captureProgram, "vge_occResolution", occRes);

            int gx = (tileSize + 7) / 8;
            int gy = (tileSize + 7) / 8;
            GL.DispatchCompute(gx, gy, captureCount);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        Assert.Equal(desiredPages, virtualToPhysical.Count);

        // Read back material atlas and validate that every allocated physical page has non-zero oct-normal in its tile.
        byte[] rgba = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount);

        for (uint physicalPageId = 1; physicalPageId <= (uint)desiredPages; physicalPageId++)
        {
            DecodePhysicalId(physicalPageId, tilesPerAtlas, tilesPerAxis, out int atlasIndex, out int tileX, out int tileY);

            int texelX = tileX * tileSize;
            int texelY = tileY * tileSize;

            int idx = (((atlasIndex * atlasH) + texelY) * atlasW + texelX) * 4;
            byte r = rgba[idx + 0];
            byte g = rgba[idx + 1];
            Assert.True((r | g) != 0, $"Expected tile for physicalPageId={physicalPageId} to be written (oct-normal!=0).");
        }

        GL.DeleteProgram(markProgram);
        GL.DeleteProgram(compactProgram);
        GL.DeleteProgram(captureProgram);
    }

    [Fact]
    public void PatchIdFeedback_When100ChunksVisible_ChunkSlotLayeredStamp_ReturnsPagesForAllSlots()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int markProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_mark_pages.csh");
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");

        const int chunksVisible = 100;
        const int pagesPerChunk = 16;
        const int totalPixels = chunksVisible * pagesPerChunk;

        const int gW = 160;
        const int gH = 10;
        Assert.True(gW * gH >= totalPixels);

        using var patchIdGBuffer = Texture2D.Create(gW, gH, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

        int chunkSlotCount = chunksVisible;
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

        uint[] pidTexels = new uint[gW * gH * 4];
        for (int c = 0; c < chunksVisible; c++)
        {
            for (int p = 0; p < pagesPerChunk; p++)
            {
                int i = c * pagesPerChunk + p;
                int idx = i * 4;
                pidTexels[idx + 0] = (uint)c;          // chunkSlot
                pidTexels[idx + 1] = (uint)(p + 1);    // patchId (same 1..16 for every chunk)
            }
        }
        patchIdGBuffer.UploadDataImmediate(pidTexels);

        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: totalPixels);
        using var pageRequestCounter = CreateAtomicCounterBuffer(counterCount: 1);

        GL.UseProgram(markProgram);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
        BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
        SetUniform1ui(markProgram, "vge_frameStamp", 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GL.UseProgram(compactProgram);
        pageRequestCounter.BindBase(bindingIndex: 0);
        pageRequests.BindBase(bindingIndex: 0);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadOnly, format: SizedInternalFormat.R32ui);
        SetUniform1ui(compactProgram, "vge_maxRequests", (uint)totalPixels);
        SetUniform1ui(compactProgram, "vge_frameStamp", 1u);
        SetUniform1ui(compactProgram, "vge_scanOffset", 0u);
        GL.DispatchCompute((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
        Assert.Equal((uint)totalPixels, requestCount);

        LumonScenePageRequestGpu[] requests = ReadSsbo<LumonScenePageRequestGpu>(pageRequests, itemCount: (int)requestCount);

        int[] countsBySlot = new int[chunksVisible];
        for (int i = 0; i < requests.Length; i++)
        {
            uint slot = requests[i].ChunkSlot;
            Assert.True(slot < (uint)chunksVisible, $"Unexpected chunkSlot={slot}");
            countsBySlot[(int)slot]++;
        }

        for (int c = 0; c < chunksVisible; c++)
        {
            Assert.Equal(pagesPerChunk, countsBySlot[c]);
        }

        GL.DeleteProgram(markProgram);
        GL.DeleteProgram(compactProgram);
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

    private static void SetUniform1ui(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static void SetUniform1i(int program, string name, int value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static void SetUniform3i(int program, string name, int x, int y, int z)
    {
        int loc = GL.GetUniformLocation(program, name);
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

    private static GpuAtomicCounterBuffer CreateAtomicCounterBuffer(int counterCount)
    {
        var buf = GpuAtomicCounterBuffer.Create(BufferUsageHint.DynamicDraw, debugName: "Test_AtomicCounter");
        buf.InitializeCounters(counterCount, initialValue: 0u);
        return buf;
    }

    private static void ResetAtomicCounter(GpuAtomicCounterBuffer buffer, int counterIndex)
    {
        Span<uint> zero = stackalloc uint[1] { 0u };
        buffer.UploadSubData((ReadOnlySpan<uint>)zero, dstOffsetBytes: counterIndex * sizeof(uint));
    }

    private static uint ReadAtomicCounter(GpuAtomicCounterBuffer buffer, int counterIndex)
    {
        using var mapped = buffer.MapRange<uint>(dstOffsetBytes: counterIndex * sizeof(uint), elementCount: 1, access: MapBufferAccessMask.MapReadBit);
        Assert.True(mapped.IsMapped);
        return mapped.Span[0];
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        ssbo.Allocate(checked(data.Length * Unsafe.SizeOf<T>()));
        ssbo.UploadSubData(data, dstOffsetBytes: 0);
        return ssbo;
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, int capacityItems) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        ssbo.Allocate(checked(capacityItems * Unsafe.SizeOf<T>()));
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

    private static void DecodePhysicalId(uint physicalPageId, int tilesPerAtlas, int tilesPerAxis, out int atlasIndex, out int tileX, out int tileY)
    {
        int pageIndex = (int)physicalPageId - 1;
        atlasIndex = pageIndex / tilesPerAtlas;
        int local = pageIndex - (atlasIndex * tilesPerAtlas);
        tileY = local / tilesPerAxis;
        tileX = local - (tileY * tilesPerAxis);
    }

    private sealed class NoopPageTableWriter : ILumonScenePageTableWriter
    {
        public void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry) { }
    }
}
