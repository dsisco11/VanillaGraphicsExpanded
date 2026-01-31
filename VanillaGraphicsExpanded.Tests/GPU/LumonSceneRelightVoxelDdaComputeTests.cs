using System;
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
public sealed class LumonSceneRelightVoxelDdaComputeTests : RenderTestBase
{
    public LumonSceneRelightVoxelDdaComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void Relight_HitProducesNonZeroIrradiance_AndWeightIncrements()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_relight_voxel_dda.csh");

        const int tileSize = 8;
        const int tilesPerAxis = 1;
        const int tilesPerAtlas = 1;
        const int atlasCount = 1;
        const int occRes = 32;

        // Inputs: depth/material atlases (sampled).
        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        // TraceScene occupancy: fill entire volume as solid so DDA hits deterministically.
        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 0);
        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, packed);

        // LUTs: lightId=1 -> red; scalars map [0..32] -> i/32.
        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f); // disable sun

        // Output irradiance atlas.
        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        // Choose a seed that keeps the pseudo worldCell away from volume edges to avoid out-of-bounds early-out.
        FindSafeSeedForOccRes(
            occRes,
            physicalPageId: 1u,
            virtualPageIndex: 0u,
            out uint patchId);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);

        GL.UseProgram(program);

        // SSBO binding matches shader: binding=0.
        workSsbo.BindBase(bindingIndex: 0);
        debugCounter.BindBase(bindingIndex: 1);

        // Samplers use layout(binding=N): bind textures to those units.
        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);

        // Output image binding matches shader: layout(binding=0, rgba16f) image2DArray.
        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        // Uniforms.
        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", (uint)tilesPerAxis);
        SetUniform(program, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
        _ = TrySetUniform(program, "vge_borderTexels", 0u); // may be optimized out

        SetUniform(program, "vge_frameIndex", 0);
        SetUniform(program, "vge_texelsPerPagePerFrame", (uint)(tileSize * tileSize)); // full update
        SetUniform(program, "vge_raysPerTexel", 1u);
        SetUniform(program, "vge_maxDdaSteps", 16u);
        SetUniform(program, "vge_debugCountersEnabled", 0u);

        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform(program, "vge_occResolution", occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.InRange(a, 0.99f, 1.01f);
        Assert.True(r > 0.001f || g > 0.001f || b > 0.001f, $"Expected non-zero irradiance, got rgb=({r},{g},{b})");

        GL.DeleteProgram(program);
    }

    [Fact]
    public void Relight_DebugCounters_EmptyOcc_ReportsAllMisses()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_relight_voxel_dda.csh");

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 32;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, 0u);

        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        FindSafeSeedForOccRes(occRes, physicalPageId: 1u, virtualPageIndex: 0u, out uint patchId);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);

        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);
        debugCounter.UploadZeros(counterCount: 4);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        debugCounter.BindBase(bindingIndex: 1);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", 1u);
        SetUniform(program, "vge_tilesPerAtlas", 1u);
        SetUniform(program, "vge_frameIndex", 0);
        SetUniform(program, "vge_texelsPerPagePerFrame", (uint)(tileSize * tileSize));
        SetUniform(program, "vge_raysPerTexel", 1u);
        SetUniform(program, "vge_maxDdaSteps", 16u);
        SetUniform(program, "vge_debugCountersEnabled", 1u);

        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform(program, "vge_occResolution", occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.AtomicCounterBarrierBit);

        uint[] counters = debugCounter.Read(counterCount: 4);
        uint expectedRays = (uint)(tileSize * tileSize);
        Assert.Equal(expectedRays, counters[0]); // rays
        Assert.Equal(0u, counters[1]);           // hits
        Assert.Equal(expectedRays, counters[2]); // misses
        Assert.Equal(0u, counters[3]);           // oob starts

        GL.DeleteProgram(program);
    }

    [Fact]
    public void Relight_EmptyOcc_LeavesRgbBlack_ButStillIncrementsWeight()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_relight_voxel_dda.csh");

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 16;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, 0u);

        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        debugCounter.BindBase(bindingIndex: 1);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", 1u);
        SetUniform(program, "vge_tilesPerAtlas", 1u);
        SetUniform(program, "vge_frameIndex", 0);
        SetUniform(program, "vge_texelsPerPagePerFrame", (uint)(tileSize * tileSize));
        SetUniform(program, "vge_raysPerTexel", 1u);
        SetUniform(program, "vge_maxDdaSteps", 8u);
        SetUniform(program, "vge_debugCountersEnabled", 0u);
        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform(program, "vge_occResolution", occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: 0, y: 0);

        Assert.InRange(a, 0.99f, 1.01f);
        Assert.InRange(r, -1e-4f, 1e-4f);
        Assert.InRange(g, -1e-4f, 1e-4f);
        Assert.InRange(b, -1e-4f, 1e-4f);

        GL.DeleteProgram(program);
    }

    [Fact]
    public void Relight_TemporalAccumulation_IncrementsWeight_AndStaysFinite()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_relight_voxel_dda.csh");

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 32;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 0);
        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, packed);

        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        FindSafeSeedForOccRes(
            occRes,
            physicalPageId: 1u,
            virtualPageIndex: 0u,
            out uint patchId);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        debugCounter.BindBase(bindingIndex: 1);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", 1u);
        SetUniform(program, "vge_tilesPerAtlas", 1u);
        SetUniform(program, "vge_texelsPerPagePerFrame", (uint)(tileSize * tileSize));
        SetUniform(program, "vge_raysPerTexel", 1u);
        SetUniform(program, "vge_maxDdaSteps", 16u);
        SetUniform(program, "vge_debugCountersEnabled", 0u);
        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform(program, "vge_occResolution", occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;

        // Frame 0
        SetUniform(program, "vge_frameIndex", 0);
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Frame 1
        SetUniform(program, "vge_frameIndex", 1);
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.InRange(a, 1.99f, 2.01f);
        Assert.False(float.IsNaN(r) || float.IsNaN(g) || float.IsNaN(b));
        Assert.False(float.IsInfinity(r) || float.IsInfinity(g) || float.IsInfinity(b));

        GL.DeleteProgram(program);
    }

    [Fact]
    public void Relight_HalfspaceOccupancy_FrontFacingHemisphere_AlwaysHits()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_relight_voxel_dda.csh");

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 32;

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

        FindSafeSeedWithLocalZForHalfspace(
            occRes,
            physicalPageId: 1u,
            virtualPageIndex: 0u,
            out uint patchId,
            out int localZ);

        int startZ = localZ + 1; // floor(localZ + 1.01)
        uint packedSolid = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 0);

        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillOccHalfspaceZ(occ.TextureId, occRes, thresholdZ: startZ, solidForZGreaterOrEqual: true, packedSolid: packedSolid);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);

        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);
        debugCounter.UploadZeros(counterCount: 4);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        debugCounter.BindBase(bindingIndex: 1);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", 1u);
        SetUniform(program, "vge_tilesPerAtlas", 1u);
        SetUniform(program, "vge_frameIndex", 0);
        SetUniform(program, "vge_texelsPerPagePerFrame", (uint)(tileSize * tileSize));
        SetUniform(program, "vge_raysPerTexel", 1u);
        SetUniform(program, "vge_maxDdaSteps", 8u);
        SetUniform(program, "vge_debugCountersEnabled", 1u);
        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform(program, "vge_occResolution", occRes);

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

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);
        Assert.InRange(a, 0.99f, 1.01f);
        Assert.True(r > 1e-3f || g > 1e-3f || b > 1e-3f, "Expected non-zero irradiance from halfspace hits.");

        GL.DeleteProgram(program);
    }

    [Fact]
    public void Relight_OutOfBoundsStart_TreatsAsMiss_EvenIfOccHasSolidCell()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_relight_voxel_dda.csh");

        const int tileSize = 8;
        const int atlasCount = 1;

        // occRes=1 ensures the traced origin starts outside the valid volume for +Z normal due to +0.51 offset.
        const int occRes = 1;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 0);
        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, packed);

        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        debugCounter.BindBase(bindingIndex: 1);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", 1u);
        SetUniform(program, "vge_tilesPerAtlas", 1u);
        SetUniform(program, "vge_frameIndex", 0);
        SetUniform(program, "vge_texelsPerPagePerFrame", (uint)(tileSize * tileSize));
        SetUniform(program, "vge_raysPerTexel", 1u);
        SetUniform(program, "vge_maxDdaSteps", 8u);
        SetUniform(program, "vge_debugCountersEnabled", 0u);
        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform(program, "vge_occResolution", occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.InRange(a, 0.99f, 1.01f);
        Assert.InRange(r, -1e-4f, 1e-4f);
        Assert.InRange(g, -1e-4f, 1e-4f);
        Assert.InRange(b, -1e-4f, 1e-4f);

        GL.DeleteProgram(program);
    }

    private static void FindSafeSeedForOccRes(int occRes, uint physicalPageId, uint virtualPageIndex, out uint patchId)
    {
        // Need enough padding so origin.x/y offsets (≈[-2..2]) stay in bounds, and +Z offset doesn't push z outside.
        // We accept [2..res-3] for x/y and [0..res-2] for z.
        for (uint p = 1u; p < 10_000u; p++)
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

    private static void FindSafeSeedWithLocalZForHalfspace(int occRes, uint physicalPageId, uint virtualPageIndex, out uint patchId, out int localZ)
    {
        // Halfspace test needs a guaranteed in-bounds Z step:
        // startZ == floor(localZ + 1.01) == localZ + 1, so require startZ <= res-2 => localZ <= res-3.
        for (uint p = 1u; p < 100_000u; p++)
        {
            uint seedBase = Squirrel3Noise.HashU(virtualPageIndex, physicalPageId, p);
            int x = (int)(seedBase % (uint)occRes);
            int y = (int)(Squirrel3Noise.HashU(seedBase, 1u) % (uint)occRes);
            int z = (int)(Squirrel3Noise.HashU(seedBase, 2u) % (uint)occRes);

            if (x >= 2 && x <= occRes - 3 &&
                y >= 2 && y <= occRes - 3 &&
                z >= 0 && z <= occRes - 3)
            {
                patchId = p;
                localZ = z;
                return;
            }
        }

        throw new InvalidOperationException($"Unable to find a safe patchId seed for halfspace occRes={occRes}.");
    }

    private static void FillOccHalfspaceZ(int textureId, int res, int thresholdZ, bool solidForZGreaterOrEqual, uint packedSolid)
    {
        thresholdZ = Math.Clamp(thresholdZ, 0, res);

        uint[] data = new uint[checked(res * res * res)];
        int idx = 0;
        for (int z = 0; z < res; z++)
        {
            bool solid = solidForZGreaterOrEqual ? (z >= thresholdZ) : (z < thresholdZ);
            uint v = solid ? packedSolid : 0u;
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    data[idx++] = v;
                }
            }
        }

        GL.BindTexture(TextureTarget.Texture3D, textureId);
        GL.TexSubImage3D(TextureTarget.Texture3D, 0, 0, 0, 0, res, res, res, PixelFormat.RedInteger, PixelType.UnsignedInt, data);
        GL.BindTexture(TextureTarget.Texture3D, 0);
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
        string? processedSource = helper.GetProcessedSource(computeShaderFile);
        Assert.False(string.IsNullOrWhiteSpace(processedSource), $"Missing processed shader source: {computeShaderFile}");

        int shaderId = GL.CreateShader(ShaderType.ComputeShader);
        GL.ShaderSource(shaderId, processedSource);
        GL.CompileShader(shaderId);

        GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int okShader);
        if (okShader == 0)
        {
            string infoLog = GL.GetShaderInfoLog(shaderId) ?? string.Empty;
            string head = GetFirstLines(processedSource, 60);
            Assert.True(okShader != 0, $"Compilation failed for {computeShaderFile}:\n{infoLog}\n--- source head ---\n{head}");
        }

        int program = GL.CreateProgram();
        GL.AttachShader(program, shaderId);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
        string log = GL.GetProgramInfoLog(program) ?? string.Empty;
        Assert.True(ok != 0, $"Compute program link failed:\n{log}");

        return program;
    }

    private static string GetFirstLines(string src, int maxLines)
    {
        if (maxLines <= 0) return string.Empty;
        int count = 0;
        int idx = 0;
        while (idx < src.Length && count < maxLines)
        {
            int nl = src.IndexOf('\n', idx);
            if (nl < 0)
            {
                idx = src.Length;
                break;
            }

            idx = nl + 1;
            count++;
        }

        return src.Substring(0, idx);
    }

    private static void BindSampler(TextureTarget target, int unit, int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(target, textureId);
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

    private static new void SetUniform(int program, string name, int value)
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

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
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

    private static void FillMaterialNormalPlusZ(int textureId, int width, int height, int depth)
    {
        // Encode normal (0,0,1) into [0..255] as (128,128,255). Alpha=255.
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

    private static void FillMaterialNormalPlusZ16f(int textureId, int width, int height, int depth)
    {
        // Exact (0.5, 0.5, 1.0) encoding so decode produces an exact +Z normal (avoids tiny tangent Z components).
        float[] data = new float[checked(width * height * depth * 4)];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i + 0] = 0.5f;
            data[i + 1] = 0.5f;
            data[i + 2] = 1.0f;
            data[i + 3] = 1.0f;
        }

        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Rgba, PixelType.Float, data);
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

    private static void FillR32ui3D(int textureId, int width, int height, int depth, uint value)
    {
        uint[] data = new uint[checked(width * height * depth)];
        Array.Fill(data, value);

        GL.BindTexture(TextureTarget.Texture3D, textureId);
        GL.TexSubImage3D(TextureTarget.Texture3D, 0, 0, 0, 0, width, height, depth, PixelFormat.RedInteger, PixelType.UnsignedInt, data);
        GL.BindTexture(TextureTarget.Texture3D, 0);
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

    private sealed class AtomicCounterBuffer : IDisposable
    {
        private int bufferId;

        public AtomicCounterBuffer(int bufferId) => this.bufferId = bufferId;

        public void BindBase(int bindingIndex)
        {
            GL.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, bindingIndex, bufferId);
        }

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

    private static AtomicCounterBuffer CreateAtomicCounterBuffer(int counterCount)
    {
        if (counterCount <= 0) counterCount = 1;

        int id = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, id);
        GL.BufferData(BufferTarget.AtomicCounterBuffer, sizeof(uint) * counterCount, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
        return new AtomicCounterBuffer(id);
    }
}
