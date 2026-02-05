using System;
using System.IO;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonTraceSceneRegionToClipmapComputeTests : RenderTestBase
{
    public LumonTraceSceneRegionToClipmapComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void RegionToClipmap_L0_WritesExpectedPayload()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var program = ComputeProgram.Create(helper, "lumonscene_trace_scene_region_to_clipmap.csh", debugName: "Tests.LumonTraceSceneRegionToClipmap.L0");
        int programId = program.ProgramId;

        const int res = 32;
        const int regionSize = 32;
        const int regionCellCount = regionSize * regionSize * regionSize;

        using var occ = Texture3D.Create(
            width: res,
            height: res,
            depth: res,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_OccL0");

        // Clear destination to 0.
        occ.UploadDataImmediate(new uint[res * res * res], x: 0, y: 0, z: 0, regionWidth: res, regionHeight: res, regionDepth: res, mipLevel: 0);

        // Payload words: payload[linear] = linear+1 (never zero).
        uint[] payload = new uint[regionCellCount];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (uint)(i + 1);
        }

        using var payloadSsbo = CreateSsbo<uint>("Test_PayloadSSBO", payload);

        // One update targeting region (0,0,0), level 0 only.
        var upd = new RegionUpdateGpu(
            regionCoord: new VectorInt4(0, 0, 0, 0),
            srcOffsetWords: 0,
            levelMask: 1u,
            versionOrPad: 0u);

        using var updatesSsbo = CreateSsbo<RegionUpdateGpu>("Test_UpdatesSSBO", new[] { upd });

        using var atomicCounter = CreateAtomicCounterBuffer(initialValue: 1u);

        GL.UseProgram(programId);

        // SSBO bindings match the shader:
        // binding=0 payload, binding=1 updates, atomic counter binding=0.
        payloadSsbo.BindBase(bindingIndex: 0);
        updatesSsbo.BindBase(bindingIndex: 1);
        atomicCounter.BindBase(bindingIndex: 0);

        // Image binding: image unit 0 (3D images must be bound layered=true).
        occ.BindImageUnit(
            unit: 0,
            access: TextureAccess.WriteOnly,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.R32ui);

        // Uniforms.
        SetUniform(programId, "vge_levels", 1);
        SetUniform(programId, "vge_resolution", res);
        SetUniform1ui(programId, "vge_regionUpdateCount", 1u);
        SetUniform3i(programId, "vge_originMinCell[0]", 0, 0, 0);
        SetUniform3i(programId, "vge_ring[0]", 0, 0, 0);

        // Dispatch: groupsPerRegionXY=4, groupsZ=4 (one region).
        GL.DispatchCompute(4, 4, 4);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Readback and spot-check a few texels.
        uint[] outData = ReadTexImageR32ui(occ.TextureId, TextureTarget.Texture3D, res, res, res);

        static int TexIndex32(int x, int y, int z) => (z * 32 + y) * 32 + x;
        // IMPORTANT: must match shader payload word order: x | (z<<5) | (y<<10) => (y*32 + z)*32 + x.
        static int VsIndex32(int x, int y, int z) => (y * 32 + z) * 32 + x;
        static uint ExpectedAt(int x, int y, int z) => (uint)(VsIndex32(x, y, z) + 1);

        Assert.Equal(ExpectedAt(0, 0, 0), outData[TexIndex32(0, 0, 0)]);
        Assert.Equal(ExpectedAt(31, 0, 0), outData[TexIndex32(31, 0, 0)]);
        Assert.Equal(ExpectedAt(0, 31, 0), outData[TexIndex32(0, 31, 0)]);
        Assert.Equal(ExpectedAt(0, 0, 31), outData[TexIndex32(0, 0, 31)]);

        Assert.Equal(ExpectedAt(13, 7, 21), outData[TexIndex32(13, 7, 21)]);

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void RegionToClipmap_MultiLevel_UsesRepresentativeSamples_ForHigherLevels()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var program = ComputeProgram.Create(helper, "lumonscene_trace_scene_region_to_clipmap.csh", debugName: "Tests.LumonTraceSceneRegionToClipmap.MultiLevel");
        int programId = program.ProgramId;

        const int res = 32;
        const int regionSize = 32;
        const int regionCellCount = regionSize * regionSize * regionSize;

        using var occ0 = Texture3D.Create(
            width: res,
            height: res,
            depth: res,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_OccL0");

        using var occ1 = Texture3D.Create(
            width: res,
            height: res,
            depth: res,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_OccL1");

        using var occ2 = Texture3D.Create(
            width: res,
            height: res,
            depth: res,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_OccL2");

        uint[] clear = new uint[res * res * res];
        occ0.UploadDataImmediate(clear, x: 0, y: 0, z: 0, regionWidth: res, regionHeight: res, regionDepth: res, mipLevel: 0);
        occ1.UploadDataImmediate(clear, x: 0, y: 0, z: 0, regionWidth: res, regionHeight: res, regionDepth: res, mipLevel: 0);
        occ2.UploadDataImmediate(clear, x: 0, y: 0, z: 0, regionWidth: res, regionHeight: res, regionDepth: res, mipLevel: 0);

        // Payload words: payload[linear] = linear+1 (never zero).
        uint[] payload = new uint[regionCellCount];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (uint)(i + 1);
        }

        using var payloadSsbo = CreateSsbo<uint>("Test_PayloadSSBO", payload);

        // One update targeting region (0,0,0), update levels 0..2.
        var upd = new RegionUpdateGpu(
            regionCoord: new VectorInt4(0, 0, 0, 0),
            srcOffsetWords: 0,
            levelMask: 0b111u,
            versionOrPad: 0u);

        using var updatesSsbo = CreateSsbo<RegionUpdateGpu>("Test_UpdatesSSBO", new[] { upd });
        using var atomicCounter = CreateAtomicCounterBuffer(initialValue: 1u);

        GL.UseProgram(programId);

        payloadSsbo.BindBase(bindingIndex: 0);
        updatesSsbo.BindBase(bindingIndex: 1);
        atomicCounter.BindBase(bindingIndex: 0);

        // Bind all levels. Use layered=true for 3D images (regression guard for z>0 slice writes).
        occ0.BindImageUnit(unit: 0, access: TextureAccess.WriteOnly, level: 0, layered: true, layer: 0, format: SizedInternalFormat.R32ui);
        occ1.BindImageUnit(unit: 1, access: TextureAccess.WriteOnly, level: 0, layered: true, layer: 0, format: SizedInternalFormat.R32ui);
        occ2.BindImageUnit(unit: 2, access: TextureAccess.WriteOnly, level: 0, layered: true, layer: 0, format: SizedInternalFormat.R32ui);

        SetUniform(programId, "vge_levels", 3);
        SetUniform(programId, "vge_resolution", res);
        SetUniform1ui(programId, "vge_regionUpdateCount", 1u);

        // Identity mapping for all levels (region only covers 0..31 anyway).
        for (int level = 0; level < 3; level++)
        {
            SetUniform3i(programId, $"vge_originMinCell[{level}]", 0, 0, 0);
            SetUniform3i(programId, $"vge_ring[{level}]", 0, 0, 0);
        }

        GL.DispatchCompute(4, 4, 4);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        uint[] outL1 = ReadTexImageR32ui(occ1.TextureId, TextureTarget.Texture3D, res, res, res);
        uint[] outL2 = ReadTexImageR32ui(occ2.TextureId, TextureTarget.Texture3D, res, res, res);

        static int TexIndex32(int x, int y, int z) => (z * 32 + y) * 32 + x;
        // IMPORTANT: must match shader payload word order: x | (z<<5) | (y<<10) => (y*32 + z)*32 + x.
        static int VsIndex32(int x, int y, int z) => (y * 32 + z) * 32 + x;
        static uint ExpectedPayloadAtLocal(int x, int y, int z) => (uint)(VsIndex32(x, y, z) + 1);

        static void AssertPayloadFromBlock(uint actual, int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
        {
            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                if (actual == ExpectedPayloadAtLocal(x, y, z))
                {
                    return;
                }
            }

            Assert.Fail($"Payload {actual} not in expected block [{minX}..{maxX}]x[{minY}..{maxY}]x[{minZ}..{maxZ}]");
        }

        // Level 1 (spacing=2): value must come from the corresponding 2x2x2 block.
        AssertPayloadFromBlock(outL1[TexIndex32(0, 0, 0)], minX: 0, maxX: 1, minY: 0, maxY: 1, minZ: 0, maxZ: 1);
        AssertPayloadFromBlock(outL1[TexIndex32(1, 0, 0)], minX: 2, maxX: 3, minY: 0, maxY: 1, minZ: 0, maxZ: 1);
        AssertPayloadFromBlock(outL1[TexIndex32(0, 1, 2)], minX: 0, maxX: 1, minY: 2, maxY: 3, minZ: 4, maxZ: 5);
        AssertPayloadFromBlock(outL1[TexIndex32(15, 15, 15)], minX: 30, maxX: 31, minY: 30, maxY: 31, minZ: 30, maxZ: 31);

        // No representative sample exists for levelCell >= 16 in this single 32^3 region.
        Assert.Equal(0u, outL1[TexIndex32(16, 0, 0)]);
        Assert.Equal(0u, outL1[TexIndex32(31, 31, 31)]);

        // Level 2 (spacing=4): value must come from the corresponding 4x4x4 block.
        AssertPayloadFromBlock(outL2[TexIndex32(0, 0, 0)], minX: 0, maxX: 3, minY: 0, maxY: 3, minZ: 0, maxZ: 3);
        AssertPayloadFromBlock(outL2[TexIndex32(1, 0, 0)], minX: 4, maxX: 7, minY: 0, maxY: 3, minZ: 0, maxZ: 3);
        AssertPayloadFromBlock(outL2[TexIndex32(0, 1, 2)], minX: 0, maxX: 3, minY: 4, maxY: 7, minZ: 8, maxZ: 11);
        AssertPayloadFromBlock(outL2[TexIndex32(7, 7, 7)], minX: 28, maxX: 31, minY: 28, maxY: 31, minZ: 28, maxZ: 31);

        Assert.Equal(0u, outL2[TexIndex32(8, 0, 0)]);

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void RegionToClipmap_L0_NegativeCoords_AndRingWrap_MatchesCpuMath()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var program = ComputeProgram.Create(helper, "lumonscene_trace_scene_region_to_clipmap.csh", debugName: "Tests.LumonTraceSceneRegionToClipmap.NegWrap");
        int programId = program.ProgramId;

        const int res = 32;
        const int regionSize = 32;
        const int regionCellCount = regionSize * regionSize * regionSize;

        using var occ = Texture3D.Create(
            width: res,
            height: res,
            depth: res,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_OccL0_NegWrap");

        occ.UploadDataImmediate(new uint[res * res * res], x: 0, y: 0, z: 0, regionWidth: res, regionHeight: res, regionDepth: res, mipLevel: 0);

        // Payload words: payload[linear] = linear+1 (never zero).
        uint[] payload = new uint[regionCellCount];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (uint)(i + 1);
        }

        using var payloadSsbo = CreateSsbo<uint>("Test_PayloadSSBO", payload);

        // One update targeting region (-1,-1,-1), level 0 only.
        var upd = new RegionUpdateGpu(
            regionCoord: new VectorInt4(-1, -1, -1, 0),
            srcOffsetWords: 0,
            levelMask: 1u,
            versionOrPad: 0u);

        using var updatesSsbo = CreateSsbo<RegionUpdateGpu>("Test_UpdatesSSBO", new[] { upd });
        using var atomicCounter = CreateAtomicCounterBuffer(initialValue: 1u);

        GL.UseProgram(programId);

        payloadSsbo.BindBase(bindingIndex: 0);
        updatesSsbo.BindBase(bindingIndex: 1);
        atomicCounter.BindBase(bindingIndex: 0);

        // Bind with layered=true to ensure writes land at z>0 slices as well.
        occ.BindImageUnit(unit: 0, access: TextureAccess.WriteOnly, level: 0, layered: true, layer: 0, format: SizedInternalFormat.R32ui);

        // OriginMin chosen so region world cells [-32..-1] are in-bounds. Ring is intentionally negative on X/Z.
        var originMin = new VectorInt3(-32, -32, -32);
        var ring = new VectorInt3(-3, 5, -7);

        SetUniform(programId, "vge_levels", 1);
        SetUniform(programId, "vge_resolution", res);
        SetUniform1ui(programId, "vge_regionUpdateCount", 1u);
        SetUniform3i(programId, "vge_originMinCell[0]", originMin.X, originMin.Y, originMin.Z);
        SetUniform3i(programId, "vge_ring[0]", ring.X, ring.Y, ring.Z);

        GL.DispatchCompute(4, 4, 4);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        uint[] outData = ReadTexImageR32ui(occ.TextureId, TextureTarget.Texture3D, res, res, res);

        static int TexIndex32(int x, int y, int z) => (z * 32 + y) * 32 + x;
        // IMPORTANT: must match shader payload word order: x | (z<<5) | (y<<10) => (y*32 + z)*32 + x.
        static int VsIndex32(int x, int y, int z) => (y * 32 + z) * 32 + x;

        static uint ExpectedPayloadAtLocal(int x, int y, int z) => (uint)(VsIndex32(x, y, z) + 1);

        // Spot-check a few world cells in the region and validate the shader's mapping matches CPU math.
        Assert.True(LumonSceneTraceSceneClipmapMath.TryMapLevelCellToTexel(
            levelCell: new VectorInt3(-32, -32, -32),
            originMinCell: originMin,
            ring: ring,
            resolution: res,
            out VectorInt3 tex0));
        Assert.Equal(ExpectedPayloadAtLocal(0, 0, 0), outData[TexIndex32(tex0.X, tex0.Y, tex0.Z)]);

        Assert.True(LumonSceneTraceSceneClipmapMath.TryMapLevelCellToTexel(
            levelCell: new VectorInt3(-1, -1, -1),
            originMinCell: originMin,
            ring: ring,
            resolution: res,
            out VectorInt3 tex1));
        Assert.Equal(ExpectedPayloadAtLocal(31, 31, 31), outData[TexIndex32(tex1.X, tex1.Y, tex1.Z)]);

        Assert.True(LumonSceneTraceSceneClipmapMath.TryMapLevelCellToTexel(
            levelCell: new VectorInt3(-17, -9, -23),
            originMinCell: originMin,
            ring: ring,
            resolution: res,
            out VectorInt3 tex2));
        Assert.Equal(ExpectedPayloadAtLocal(15, 23, 9), outData[TexIndex32(tex2.X, tex2.Y, tex2.Z)]);

        // Program disposed via ComputeProgram.
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RegionUpdateGpu
    {
        public readonly VectorInt4 RegionCoord; // shader uses ivec3, std430 aligns vec3 to 16; vec4 is safe
        public readonly uint SrcOffsetWords;
        public readonly uint LevelMask;
        public readonly uint VersionOrPad;
        public readonly uint Padding0;

        public RegionUpdateGpu(in VectorInt4 regionCoord, uint srcOffsetWords, uint levelMask, uint versionOrPad)
        {
            RegionCoord = regionCoord;
            SrcOffsetWords = srcOffsetWords;
            LevelMask = levelMask;
            VersionOrPad = versionOrPad;
            Padding0 = 0;
        }
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

    private static GpuShaderStorageBuffer CreateSsbo<T>(string debugName, T[] data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: debugName);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
    }

    private sealed class AtomicCounterBuffer : IDisposable
    {
        private int bufferId;

        public AtomicCounterBuffer(int bufferId) => this.bufferId = bufferId;

        public void BindBase(int bindingIndex)
        {
            GL.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, bufferId);
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

    private static uint[] ReadTexImageR32ui(int textureId, TextureTarget target, int width, int height, int depth)
    {
        uint[] data = new uint[checked(width * height * depth)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(target, textureId);
        GL.GetTexImage(target, level: 0, PixelFormat.RedInteger, PixelType.UnsignedInt, data);
        GL.BindTexture(target, 0);
        return data;
    }
}
