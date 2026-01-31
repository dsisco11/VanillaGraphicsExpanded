using System;
using System.IO;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonTraceSceneToRelightIntegrationTests : RenderTestBase
{
    public LumonTraceSceneToRelightIntegrationTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void RegionToClipmapThenRelight_SolidPayload_ProducesAllHits()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int regionToClipProgram = CompileAndLinkCompute(helper, "lumonscene_trace_scene_region_to_clipmap.csh");
        int relightProgram = CompileAndLinkCompute(helper, "lumonscene_relight_voxel_dda.csh");

        // Clipmap config: single level, 32^3, origin/ring at 0 (no wrap offsets).
        const int res = 32;
        const int levels = 1;
        const int regionCellCount = 32 * 32 * 32;

        // Fill region payload with a non-zero solid value (lightId=1 => red).
        uint solidPayload = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);
        uint[] payload = new uint[regionCellCount];
        Array.Fill(payload, solidPayload);

        using var payloadSsbo = CreateSsbo<uint>("Test_RegionPayload", payload);

        var upd = new RegionUpdateGpu(
            regionCoord: new VectorInt4(0, 0, 0, 0),
            srcOffsetWords: 0,
            levelMask: 1u,
            versionOrPad: 0u);

        using var updatesSsbo = CreateSsbo<RegionUpdateGpu>("Test_RegionUpdates", new[] { upd });
        using var updateCountCounter = CreateAtomicCounterBuffer(initialValue: 1u);

        using var occL0 = Texture3D.Create(
            width: res,
            height: res,
            depth: res,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_OccL0");

        // Clear destination first.
        occL0.UploadDataImmediate(new uint[res * res * res], x: 0, y: 0, z: 0, regionWidth: res, regionHeight: res, regionDepth: res, mipLevel: 0);

        // Dispatch region->clipmap update.
        GL.UseProgram(regionToClipProgram);
        payloadSsbo.BindBase(bindingIndex: 0);
        updatesSsbo.BindBase(bindingIndex: 1);
        updateCountCounter.BindBase(bindingIndex: 0);

        occL0.BindImageUnit(unit: 0, access: TextureAccess.WriteOnly, level: 0, layered: false, layer: 0, format: SizedInternalFormat.R32ui);

        SetUniform(regionToClipProgram, "vge_levels", levels);
        SetUniform(regionToClipProgram, "vge_resolution", res);
        SetUniform3i(regionToClipProgram, "vge_originMinCell[0]", 0, 0, 0);
        SetUniform3i(regionToClipProgram, "vge_ring[0]", 0, 0, 0);

        GL.DispatchCompute(4, 4, 4); // one region
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Relight config: 1 physical page, 1 tile, small tile size.
        const int tileSize = 8;
        const int atlasCount = 1;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        // Choose a safe seed away from bounds, matching relight's pseudo-origin selection.
        FindSafeSeedForOccRes(res, physicalPageId: 1u, virtualPageIndex: 0u, out uint patchId);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_RelightWork", work);

        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);
        debugCounter.UploadZeros(counterCount: 4);

        GL.UseProgram(relightProgram);
        workSsbo.BindBase(bindingIndex: 0);
        debugCounter.BindBase(bindingIndex: 1);

        // Bind samplers/images through the production program-layout cache (matches runtime binding behavior).
        var layout = GpuProgramLayout.TryBuild(relightProgram);
        Assert.True(layout.SamplerBindings.Count > 0, "GpuProgramLayout.TryBuild produced no sampler bindings for the relight program.");
        Assert.True(layout.ImageBindings.Count > 0, "GpuProgramLayout.TryBuild produced no image bindings for the relight program.");

        // Note: vge_depthAtlas may be optimized out in the current relight shader; treat it as optional.
        if (layout.SamplerBindings.ContainsKey("vge_depthAtlas"))
        {
            Assert.True(layout.TryBindSamplerTexture("vge_depthAtlas", TextureTarget.Texture2DArray, depthAtlas.TextureId), $"Failed to bind vge_depthAtlas. Known: {FormatKeys(layout.SamplerBindings)}");
        }
        Assert.True(layout.TryBindSamplerTexture("vge_materialAtlas", TextureTarget.Texture2DArray, materialAtlas.TextureId), $"Missing sampler binding for vge_materialAtlas. Known: {FormatKeys(layout.SamplerBindings)}");
        Assert.True(layout.TryBindSamplerTexture("vge_occL0", TextureTarget.Texture3D, occL0.TextureId), $"Missing sampler binding for vge_occL0. Known: {FormatKeys(layout.SamplerBindings)}");
        Assert.True(layout.TryBindSamplerTexture("vge_lightColorLut", TextureTarget.Texture2D, lightColorLut.TextureId), $"Missing sampler binding for vge_lightColorLut. Known: {FormatKeys(layout.SamplerBindings)}");
        Assert.True(layout.TryBindSamplerTexture("vge_blockLevelScalarLut", TextureTarget.Texture2D, blockScalar.TextureId), $"Missing sampler binding for vge_blockLevelScalarLut. Known: {FormatKeys(layout.SamplerBindings)}");
        Assert.True(layout.TryBindSamplerTexture("vge_sunLevelScalarLut", TextureTarget.Texture2D, sunScalar.TextureId), $"Missing sampler binding for vge_sunLevelScalarLut. Known: {FormatKeys(layout.SamplerBindings)}");
        Assert.True(layout.TryBindImageTexture(
            "vge_irradianceAtlas",
            irradiance,
            access: TextureAccess.ReadWrite,
            level: 0,
            layered: true,
            layer: 0,
            formatOverride: SizedInternalFormat.Rgba16f), $"Missing image binding for vge_irradianceAtlas. Known: {FormatKeys(layout.ImageBindings)}");

        SetUniform(relightProgram, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(relightProgram, "vge_tilesPerAxis", 1u);
        SetUniform(relightProgram, "vge_tilesPerAtlas", 1u);
        _ = TrySetUniform(relightProgram, "vge_borderTexels", 0u);

        SetUniform(relightProgram, "vge_frameIndex", 0);
        SetUniform(relightProgram, "vge_texelsPerPagePerFrame", (uint)(tileSize * tileSize));
        SetUniform(relightProgram, "vge_raysPerTexel", 1u);
        SetUniform(relightProgram, "vge_maxDdaSteps", 16u);
        SetUniform(relightProgram, "vge_debugCountersEnabled", 1u);

        SetUniform3i(relightProgram, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(relightProgram, "vge_occRing0", 0, 0, 0);
        SetUniform(relightProgram, "vge_occResolution", res);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        uint[] counters = debugCounter.Read(counterCount: 4);
        uint expectedRays = (uint)(tileSize * tileSize);
        Assert.Equal(expectedRays, counters[0]); // rays
        Assert.Equal(expectedRays, counters[1]); // hits
        Assert.Equal(0u, counters[2]);           // misses
        Assert.Equal(0u, counters[3]);           // oob starts

        GL.DeleteProgram(regionToClipProgram);
        GL.DeleteProgram(relightProgram);
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

    private sealed class AtomicCounterBuffer : IDisposable
    {
        private int bufferId;

        public AtomicCounterBuffer(int bufferId) => this.bufferId = bufferId;

        public void BindBase(int bindingIndex)
            => GL.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, bufferId);

        public void UploadZeros(int counterCount)
        {
            counterCount = Math.Max(1, counterCount);
            uint[] zeros = new uint[counterCount];
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, bufferId);
            GL.BufferSubData(BufferTarget.AtomicCounterBuffer, IntPtr.Zero, sizeof(uint) * counterCount, zeros);
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
        }

        public uint[] Read(int counterCount)
        {
            counterCount = Math.Max(1, counterCount);
            uint[] data = new uint[counterCount];
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, bufferId);
            GL.GetBufferSubData(BufferTarget.AtomicCounterBuffer, IntPtr.Zero, sizeof(uint) * counterCount, data);
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
            return data;
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

    private static AtomicCounterBuffer CreateAtomicCounterBuffer(int counterCount)
    {
        if (counterCount <= 0) counterCount = 1;

        int id = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, id);
        GL.BufferData(BufferTarget.AtomicCounterBuffer, sizeof(uint) * counterCount, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
        return new AtomicCounterBuffer(id);
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string debugName, T[] data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: debugName);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string debugName, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: debugName);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
    }

    private static void SetUniform3i(int program, string name, int x, int y, int z)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform3(loc, x, y, z);
    }

    private static new void SetUniform(int program, string name, int value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
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

    private static void FillMaterialNormalPlusZ(int textureId, int width, int height, int depth)
    {
        byte[] data = new byte[checked(width * height * depth * 4)];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i + 0] = 128;
            data[i + 1] = 128;
            data[i + 2] = 255;
            data[i + 3] = 255;
        }

        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static void FillR16f2DArray(int textureId, int width, int height, int depth, float value)
    {
        float[] data = new float[checked(width * height * depth)];
        Array.Fill(data, value);

        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Red, PixelType.Float, data);
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

    private static void FindSafeSeedForOccRes(int occRes, uint physicalPageId, uint virtualPageIndex, out uint patchId)
    {
        for (uint p = 1u; p < 100_000u; p++)
        {
            uint seedBase = Squirrel3Noise.HashU(virtualPageIndex, physicalPageId, p);
            int x = (int)(seedBase % (uint)occRes);
            int y = (int)(Squirrel3Noise.HashU(seedBase, 1u) % (uint)occRes);
            int z = (int)(Squirrel3Noise.HashU(seedBase, 2u) % (uint)occRes);

            if (x >= 2 && x <= occRes - 3 && y >= 2 && y <= occRes - 3 && z >= 0 && z <= occRes - 2)
            {
                patchId = p;
                return;
            }
        }

        throw new InvalidOperationException($"Unable to find a safe patchId seed for occRes={occRes}.");
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

    private static string FormatKeys(System.Collections.Generic.IReadOnlyDictionary<string, int> dict)
        => dict.Count == 0 ? "<none>" : string.Join(", ", dict.Keys);
}
