using System;
using System.Collections.Generic;
using System.IO;
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
        using var markComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_mark_pages.csh", debugName: "Tests.LumonSceneRuntimeCaptureWiring.Mark");
        using var compactComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_compact_pages.csh", debugName: "Tests.LumonSceneRuntimeCaptureWiring.Compact");
        using var captureComputeProgram = ComputeProgram.Create(helper, "lumonscene_capture_voxel.csh", debugName: "Tests.LumonSceneRuntimeCaptureWiring.Capture");
        int markProgram = markComputeProgram.ProgramId;
        int compactProgram = compactComputeProgram.ProgramId;
        int captureProgram = captureComputeProgram.ProgramId;

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

        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: desiredPages);
        using var pageRequestCounter = CreateAtomicCounterBuffer(initialValue: 0u);
        using var markCounters = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 3);

        // Pass A: mark.
        GL.UseProgram(markProgram);
        markCounters.BindBase(bindingIndex: 0);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
        BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
        SetUniform(markProgram, "vge_frameStamp", 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Pass B: compact.
        GL.UseProgram(compactProgram);
        pageRequestCounter.BindBase(bindingIndex: 0);
        pageRequests.BindBase(bindingIndex: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageUsageStamp", usageStamp.TextureId, unit: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageTableMip0", pageTableMip0.TextureId, unit: 1);
        SetUniform(compactProgram, "vge_maxRequests", (uint)desiredPages);
        SetUniform(compactProgram, "vge_frameStamp", 1u);
        SetUniform(compactProgram, "vge_scanOffset", 0u);
        SetUniform(compactProgram, "vge_compactMode", 1u);
        GL.DispatchCompute((16384 * chunkSlotCount + 255) / 256, 1, 1);
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
        var virtualToPhysical = new Dictionary<ulong, uint>();
        var physicalToVirtual = new Dictionary<uint, ulong>();
        var pageTableStats = new LumonScenePageTableStatsTracker();
        pageTableStats.Reset(newChunkSlotCount: 1);
        var cpuProc = new LumonSceneFeedbackRequestProcessor(pool, pageTableMirror, virtualToPhysical, physicalToVirtual, new NullPageTableWriter(), pageTableStats);

        var captureOut = new LumonSceneCaptureWorkGpu[desiredPages];
        var relightOut = new LumonSceneRelightWorkGpu[desiredPages];
        int recaptureCursor = 0;

        cpuProc.Process(
            requests: requests,
            maxRequestsToProcess: desiredPages,
            maxNewAllocations: desiredPages,
            maxResidentPagesPerChunkSlot: 0,
            recaptureVirtualPageKeys: ReadOnlySpan<ulong>.Empty,
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
        const int occRes = 32;
        using var occL0 = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        using var materialPalette = Texture2D.Create(width: 64, height: 1, format: PixelInternalFormat.Rgba32ui, filter: TextureFilterMode.Nearest, debugName: "Test_MaterialPalette");

        ClearR16f2DArray(depthAtlas.TextureId, atlasW, atlasH, atlasCount, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount, r: 0, g: 0, b: 0, a: 0);

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

        using var captureWorkSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_CaptureWorkSSBO", captureOut.AsSpan(0, captureCount));
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[desiredPages + 1]);
        using var slotInfoSsbo = CreateSsbo<int>("Test_ChunkSlotInfoSSBO", new int[4]);

        GL.UseProgram(captureProgram);
        captureWorkSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        slotInfoSsbo.BindBase(bindingIndex: 2);
        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        BindSampler3D(unit: 2, occL0.TextureId);
        BindSampler2D(unit: 3, materialPalette.TextureId);

        SetUniform(captureProgram, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(captureProgram, "vge_tilesPerAxis", (uint)tilesPerAxis);
        SetUniform(captureProgram, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
        _ = TrySetUniform(captureProgram, "vge_borderTexels", 0u);
        SetUniform3i(captureProgram, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(captureProgram, "vge_occRing0", 0, 0, 0);
        SetUniform1i(captureProgram, "vge_occResolution", occRes);

        int gxCap = (tileSize + 7) / 8;
        int gyCap = (tileSize + 7) / 8;
        GL.DispatchCompute(gxCap, gyCap, captureCount);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Validate: each captured tile center writes the expected surfaceId.
        byte[] mat = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount);

        for (int i = 0; i < captureCount; i++)
        {
            uint physicalPageId = captureOut[i].PhysicalPageId;
            pool.PagePool.DecodePhysicalId(physicalPageId, out ushort atlasIdx, out ushort tileX, out ushort tileY);
            int cx = tileX * tileSize + tileSize / 2;
            int cy = tileY * tileSize + tileSize / 2;

            (byte r, byte g, byte b, byte a) = ReadRgbaAt(mat, atlasW, atlasH, layer: atlasIdx, x: cx, y: cy);
            // Oct-normal encoding can legitimately hit 0/255 on one axis (e.g. -X encodes to R=0).
            Assert.True(r != 0 || g != 0, $"Expected non-zero oct normal (RG not both zero), got r={r} g={g}");
            Assert.Equal((byte)9, b);
            Assert.Equal((byte)0, a);
        }

        // Programs are disposed via ComputeProgram.
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
        return CreateAtomicCounterBuffer(initialValue, counterCount: 1);
    }

    private static AtomicCounterBuffer CreateAtomicCounterBuffer(uint initialValue, int counterCount)
    {
        if (counterCount <= 0) counterCount = 1;

        int id = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, id);
        uint[] data = new uint[counterCount];
        if (initialValue != 0u)
        {
            Array.Fill(data, initialValue);
        }
        GL.BufferData(BufferTarget.AtomicCounterBuffer, sizeof(uint) * counterCount, data, BufferUsageHint.DynamicDraw);
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

    private static void BindSampler2DUint(int program, string uniformName, int textureId, int unit)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private static void BindSampler2DArrayUint(int program, string uniformName, int textureId, int unit)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
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

    private static bool TrySetUniform(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0 && ComputeProgram.TryGetExplicitUniformLocation(program, name, out int explicitLoc))
        {
            loc = explicitLoc;
        }

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

    private static (byte R, byte G, byte B, byte A) ReadRgbaAt(byte[] rgba, int width, int height, int layer, int x, int y)
    {
        int idx = (((layer * height) + y) * width + x) * 4;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
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
}
