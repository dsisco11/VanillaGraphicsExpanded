using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonScenePipelineSmokeTests : RenderTestBase
{
    public LumonScenePipelineSmokeTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void PipelineSmoke_FeedbackToCaptureToRelight_PopulatesManyPages()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int markProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_mark_pages.csh");
        int compactProgram = CompileAndLinkCompute(helper, "lumonscene_feedback_compact_pages.csh");
        int captureProgram = CompileAndLinkCompute(helper, "lumonscene_capture_voxel.csh");
        int relightProgram = CompileAndLinkCompute(helper, "lumonscene_relight_voxel_dda.csh");

        const int tileSize = 8;
        const int tilesPerAxis = 8; // atlas dims 64x64
        const int tilesPerAtlas = tilesPerAxis * tilesPerAxis; // 64
        const int atlasCount = 1;
        const int atlasW = tileSize * tilesPerAxis;
        const int atlasH = tileSize * tilesPerAxis;

        const int occRes = 64;

        // We keep the total requested pages <= tilesPerAtlas for a 1-atlas plan.
        const int desiredPages = 32;
        const int maxNewAllocsPerFrame = 4;

        // Pool plan (CPU-only; we don't create GPU resources through the pool).
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

        // CPU-side page table + mappings.
        var pageTableMirror = new LumonScenePageTableEntry[LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk];
        var virtualToPhysical = new Dictionary<int, uint>();
        var physicalToVirtual = new Dictionary<uint, int>();
        var writer = new RecordingPageTableWriter();
        var cpuProc = new LumonSceneFeedbackRequestProcessor(pool, pageTableMirror, virtualToPhysical, physicalToVirtual, writer);

        // PatchIdGBuffer (RGBA32UI): place `desiredPages` sparse pixels, rest zero.
        const int gW = 32;
        const int gH = 32;
        using var patchIdGBuffer = Texture2D.Create(gW, gH, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

        // v2 feedback uses a per-virtual-page stamp texture to deduplicate requests.
        using var usageStamp = Texture2D.Create(128, 128, PixelInternalFormat.R32ui, debugName: "Test_PageUsageStamp");
        usageStamp.UploadDataImmediate(new uint[128 * 128]);

        // Compute patch ids that (a) map to distinct virtual pages, and (b) are "safe" for the relight tracer.
        //
        // v2 request compaction loses per-pixel patchId (by design); v1 voxel patches use the identity mapping
        // patchId == virtualPageIndex (1..12288). To keep the relight tracer deterministic, we (1) choose only
        // patchIds in that range and (2) sort requests by virtual page before CPU processing.
        uint[] patchIds = FindSafeVoxelPatchIdsForSortedRequests(desiredPages: desiredPages, occRes: occRes);

        uint[] pidTexels = new uint[gW * gH * 4];
        for (int i = 0; i < desiredPages; i++)
        {
            int x = i % gW;
            int y = i / gW;
            int idx = (y * gW + x) * 4;
            pidTexels[idx + 0] = 0u;        // chunkSlot
            pidTexels[idx + 1] = patchIds[i]; // patchId
            pidTexels[idx + 2] = 0u;
            pidTexels[idx + 3] = 0u;
        }
        patchIdGBuffer.UploadDataImmediate(pidTexels);

        // Feedback outputs (atomic counter + SSBO requests).
        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: desiredPages);
        using var pageRequestCounter = CreateAtomicCounterBuffer(initialValue: 0u);
        using var relightDebugCounter = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 4);

        // Pass A: mark pages.
        GL.UseProgram(markProgram);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
        SetUniform(markProgram, "vge_frameStamp", 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: false, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Pass B: compact stamps -> bounded request list.
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

        LumonScenePageRequestGpu[] requests = pageRequests.ReadBack(count: (int)writtenRequests);
        Array.Sort(requests, static (a, b) => a.VirtualPageIndex.CompareTo(b.VirtualPageIndex));

        // GPU textures used by capture + relight.
        using var depthAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        using var irradianceAtlas = Texture3D.Create(atlasW, atlasH, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillR16f2DArray(depthAtlas.TextureId, atlasW, atlasH, atlasCount, value: 0f);
        FillRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount, r: 0, g: 0, b: 0, a: 0);
        FillRgba16f2DArray(irradianceAtlas.TextureId, atlasW, atlasH, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        // Occupancy volume + LUTs (simple deterministic radiance).
        uint occPacked = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 0);
        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, occPacked);

        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        // Scratch output arrays (max allocations + small recapture).
        var captureOut = new LumonSceneCaptureWorkGpu[64];
        var relightOut = new LumonSceneRelightWorkGpu[64];

        int recaptureCursor = 0;
        int frameIndex = 0;

        for (int frame = 0; frame < 32 && virtualToPhysical.Count < desiredPages; frame++)
        {
            cpuProc.Process(
                requests: requests,
                maxRequestsToProcess: desiredPages,
                maxNewAllocations: maxNewAllocsPerFrame,
                recaptureVirtualPages: ReadOnlySpan<int>.Empty,
                recaptureCursor: ref recaptureCursor,
                maxRecapture: 0,
                captureWorkOut: captureOut,
                relightWorkOut: relightOut,
                captureCount: out int captureCount,
                relightCount: out int relightCount,
                stats: out _);

            Assert.Equal(captureCount, relightCount);

            if (captureCount <= 0)
            {
                break;
            }

            // Upload work SSBOs for this frame slice.
            using var captureSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_CaptureWorkSSBO", captureOut.AsSpan(0, captureCount));
            using var relightSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_RelightWorkSSBO", relightOut.AsSpan(0, relightCount));

            // Capture voxel → depth/material.
            GL.UseProgram(captureProgram);
            captureSsbo.BindBase(bindingIndex: 0);
            GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
            GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);
            SetUniform(captureProgram, "vge_tileSizeTexels", (uint)tileSize);
            SetUniform(captureProgram, "vge_tilesPerAxis", (uint)tilesPerAxis);
            SetUniform(captureProgram, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
            _ = TrySetUniform(captureProgram, "vge_borderTexels", 0u);
            GL.DispatchCompute((tileSize + 7) / 8, (tileSize + 7) / 8, captureCount);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit);

            // Relight → irradiance.
            GL.UseProgram(relightProgram);
            relightSsbo.BindBase(bindingIndex: 0);
            relightDebugCounter.BindBase(bindingIndex: 1);

            BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
            BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
            BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
            BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
            BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
            BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);
            GL.BindImageTexture(0, irradianceAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

            SetUniform(relightProgram, "vge_tileSizeTexels", (uint)tileSize);
            SetUniform(relightProgram, "vge_tilesPerAxis", (uint)tilesPerAxis);
            SetUniform(relightProgram, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
            _ = TrySetUniform(relightProgram, "vge_borderTexels", 0u);

            SetUniform(relightProgram, "vge_frameIndex", frameIndex++);
            SetUniform(relightProgram, "vge_texelsPerPagePerFrame", (uint)(tileSize * tileSize));
            SetUniform(relightProgram, "vge_raysPerTexel", 1u);
            SetUniform(relightProgram, "vge_maxDdaSteps", 16u);
            _ = TrySetUniform(relightProgram, "vge_debugCountersEnabled", 0u);
            SetUniform3i(relightProgram, "vge_occOriginMinCell0", 0, 0, 0);
            SetUniform3i(relightProgram, "vge_occRing0", 0, 0, 0);
            SetUniform(relightProgram, "vge_occResolution", occRes);

            GL.DispatchCompute((tileSize + 7) / 8, (tileSize + 7) / 8, relightCount);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        Assert.Equal(desiredPages, virtualToPhysical.Count);

        // Validate: many pages have non-zero weight and non-zero RGB at the tile center.
        float[] rgba = ReadTexImageRgba16f_2DArray(irradianceAtlas.TextureId, atlasW, atlasH, atlasCount);

        int pagesWithWeight = 0;
        int pagesWithRgb = 0;
        foreach (uint pid in virtualToPhysical.Values)
        {
            pool.PagePool.DecodePhysicalId(pid, out ushort atlasIdx, out ushort tileX, out ushort tileY);
            int cx = tileX * tileSize + tileSize / 2;
            int cy = tileY * tileSize + tileSize / 2;
            (float r, float g, float b, float a) = SampleRgba(rgba, atlasW, atlasH, layer: atlasIdx, x: cx, y: cy);

            if (a > 0.5f) pagesWithWeight++;
            if (r > 1e-3f || g > 1e-3f || b > 1e-3f) pagesWithRgb++;
        }

        Assert.True(pagesWithWeight >= desiredPages - 1, $"Expected most pages to have weight, got {pagesWithWeight}/{desiredPages}");
        Assert.True(pagesWithRgb >= (desiredPages * 3) / 4, $"Expected most pages to have RGB, got {pagesWithRgb}/{desiredPages}");

        GL.DeleteProgram(markProgram);
        GL.DeleteProgram(compactProgram);
        GL.DeleteProgram(captureProgram);
        GL.DeleteProgram(relightProgram);
    }

    private static uint[] FindSafeVoxelPatchIdsForSortedRequests(int desiredPages, int occRes)
    {
        if (desiredPages <= 0) return [];

        // v1 voxel patchId encoding produces patchIds in [1..12288]. In v1/v2 we map:
        //   virtualPageIndex = patchId % (128*128) == patchId
        // Keep the selection deterministic and within that expected range.
        const uint minPatchId = 1u;
        const uint maxPatchId = 12288u;

        uint[] patchIds = new uint[desiredPages];
        int found = 0;

        for (uint patchId = minPatchId; patchId <= maxPatchId && found < desiredPages; patchId++)
        {
            uint virtualPageIndex = patchId;
            uint physicalPageIdAssigned = (uint)(desiredPages - found); // deterministic LIFO allocation order

            uint seedBase = Squirrel3Noise.HashU(virtualPageIndex, physicalPageIdAssigned, patchId);
            int x = (int)(seedBase % (uint)occRes);
            int y = (int)(Squirrel3Noise.HashU(seedBase, 1u) % (uint)occRes);
            int z = (int)(Squirrel3Noise.HashU(seedBase, 2u) % (uint)occRes);

            // Keep the pseudo-surface origin away from occupancy bounds (matches relight shader's +/-2 tangent offsets).
            if (x >= 2 && x <= occRes - 3 && y >= 2 && y <= occRes - 3 && z >= 0 && z <= occRes - 2)
            {
                patchIds[found++] = patchId;
            }
        }

        if (found != desiredPages)
        {
            throw new InvalidOperationException($"Unable to find {desiredPages} safe voxel patchIds for occRes={occRes}. Found={found}.");
        }

        return patchIds;
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

    private static void BindSampler(TextureTarget target, int unit, int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(target, textureId);
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

    private static new void SetUniform(int program, string name, int value)
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

    private static void SetUniform3i(int program, string name, int x, int y, int z)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform3(loc, x, y, z);
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
        // Leave contents undefined; shader will write the first N entries.
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

    private static void FillRgba16f2DArray(int textureId, int width, int height, int depth, float r, float g, float b, float a)
    {
        float[] data = new float[checked(width * height * depth * 4)];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i + 0] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = a;
        }
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Rgba, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static void FillR32ui3D(int textureId, int width, int height, int depth, uint value)
    {
        uint[] data = new uint[checked(width * height * depth)];
        Array.Fill(data, value);
        GL.BindTexture(TextureTarget.Texture3D, textureId);
        GL.TexSubImage3D(TextureTarget.Texture3D, 0, 0, 0, 0, width, height, depth, PixelFormat.RedInteger, PixelType.UnsignedInt, data);
        GL.BindTexture(TextureTarget.Texture3D, 0);
    }

    private static void UploadLightColorLut(Texture2D lut, int redId)
    {
        float[] data = new float[64 * 4];
        for (int i = 0; i < 64; i++)
        {
            int o = i * 4;
            data[o + 0] = 0f;
            data[o + 1] = 0f;
            data[o + 2] = 0f;
            data[o + 3] = 1f;
        }
        if ((uint)redId < 64u)
        {
            int o = redId * 4;
            data[o + 0] = 1f;
            data[o + 1] = 0f;
            data[o + 2] = 0f;
            data[o + 3] = 1f;
        }
        lut.UploadDataImmediate(data);
    }

    private static void UploadLinearScalarLut(Texture2D lut, float scale)
    {
        float[] data = new float[33];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (i / 32f) * scale;
        }
        lut.UploadDataImmediate(data);
    }

    private static float[] ReadTexImageRgba16f_2DArray(int textureId, int width, int height, int depth)
    {
        float[] data = new float[checked(width * height * depth * 4)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.GetTexImage(TextureTarget.Texture2DArray, level: 0, PixelFormat.Rgba, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
        return data;
    }

    private static (float R, float G, float B, float A) SampleRgba(float[] rgba, int width, int height, int layer, int x, int y)
    {
        int idx = (((layer * height) + y) * width + x) * 4;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
    }

    private sealed class RecordingPageTableWriter : ILumonScenePageTableWriter
    {
        public int WriteCount { get; private set; }

        public void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry)
        {
            _ = chunkSlot;
            _ = virtualPageIndex;
            _ = packedEntry;
            WriteCount++;
        }
    }
}
