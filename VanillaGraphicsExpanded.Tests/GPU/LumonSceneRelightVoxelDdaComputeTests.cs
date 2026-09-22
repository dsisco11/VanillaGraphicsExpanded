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

    private const int RelightParamsUboSizeBytes = 80;

    [Fact]
    public void Relight_HitProducesNonZeroIrradiance_AndWeightIncrements()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_relight_voxel_dda.csh", debugName: "Tests.RelightVoxelDda.Hit");
        int program = computeProgram.ProgramId;

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
        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);
        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, packed);

        // LUTs: lightId=1 -> red; scalars map [0..32] -> i/32.
        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        using var materialPalette = Texture2D.Create(16384, 1, PixelInternalFormat.Rgba32ui, debugName: "Test_MaterialPalette");
        using var surfaceLut = Texture2D.Create(256, 256, PixelInternalFormat.Rgba32ui, debugName: "Test_SurfaceLut");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f); // disable sun
        UploadMaterialPaletteAndSurfaceLut(materialPalette, surfaceLut);

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
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneRelightVoxelDda.Hit.ParamsUBO");

        GL.UseProgram(program);
        using var sharedSurface = new SharedSurfaceInputFixture(program, occ, materialPalette);

        // SSBO binding matches shader: binding=0.
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        debugCounter.BindBase(bindingIndex: 0);

        // Samplers use layout(binding=N): bind textures to those units.
        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 6, materialPalette.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 7, surfaceLut.TextureId);

        // Output image binding matches shader: layout(binding=0, rgba16f) image2DArray.
        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        BindRelightParamsUbo(
            paramsUbo,
            tileSizeTexels: tileSize,
            tilesPerAxis: tilesPerAxis,
            tilesPerAtlas: tilesPerAtlas,
            borderTexels: 0,
            frameIndex: 0,
            occResolution: occRes,
            texelsPerPagePerFrame: (uint)(tileSize * tileSize),
            raysPerTexel: 1u,
            maxDdaSteps: 16u,
            debugCountersEnabled: 0u,
            occOriginMinCell0X: 0,
            occOriginMinCell0Y: 0,
            occOriginMinCell0Z: 0,
            occRing0X: 0,
            occRing0Y: 0,
            occRing0Z: 0);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 1)");

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.InRange(a, 0.99f, 1.01f);
        Assert.True(r > 0.001f || g > 0.001f || b > 0.001f, $"Expected non-zero irradiance, got rgb=({r},{g},{b})");

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void Relight_DebugCounters_EmptyOcc_ReportsAllMisses()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_relight_voxel_dda.csh", debugName: "Tests.RelightVoxelDda.LightId");
        int program = computeProgram.ProgramId;

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
        using var materialPalette = Texture2D.Create(16384, 1, PixelInternalFormat.Rgba32ui, debugName: "Test_MaterialPalette");
        using var surfaceLut = Texture2D.Create(256, 256, PixelInternalFormat.Rgba32ui, debugName: "Test_SurfaceLut");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        UploadMaterialPaletteAndSurfaceLut(materialPalette, surfaceLut);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        FindSafeSeedForOccRes(occRes, physicalPageId: 1u, virtualPageIndex: 0u, out uint patchId);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);

        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);
        debugCounter.UploadZeros(counterCount: 4);

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneRelightVoxelDda.DebugCounters.ParamsUBO");

        GL.UseProgram(program);
        using var sharedSurface = new SharedSurfaceInputFixture(program, occ, materialPalette);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        debugCounter.BindBase(bindingIndex: 0);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 6, materialPalette.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 7, surfaceLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 6, materialPalette.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 7, surfaceLut.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        BindRelightParamsUbo(
            paramsUbo,
            tileSizeTexels: tileSize,
            tilesPerAxis: 1,
            tilesPerAtlas: 1,
            borderTexels: 0,
            frameIndex: 0,
            occResolution: occRes,
            texelsPerPagePerFrame: (uint)(tileSize * tileSize),
            raysPerTexel: 1u,
            maxDdaSteps: 16u,
            debugCountersEnabled: 1u,
            occOriginMinCell0X: 0,
            occOriginMinCell0Y: 0,
            occOriginMinCell0Z: 0,
            occRing0X: 0,
            occRing0Y: 0,
            occRing0Z: 0);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.AtomicCounterBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (atomic counter)");

        uint[] counters = debugCounter.Read(counterCount: 4);
        uint expectedRays = (uint)(tileSize * tileSize);
        Assert.Equal(expectedRays, counters[0]); // rays
        Assert.Equal(0u, counters[1]);           // hits
        Assert.Equal(expectedRays, counters[2]); // misses
        Assert.Equal(0u, counters[3]);           // oob starts

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void Relight_EmptyOcc_LeavesRgbBlack_WithoutAddingWeight()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_relight_voxel_dda.csh", debugName: "Tests.RelightVoxelDda.Miss");
        int program = computeProgram.ProgramId;

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
        using var materialPalette = Texture2D.Create(16384, 1, PixelInternalFormat.Rgba32ui, debugName: "Test_MaterialPalette");
        using var surfaceLut = Texture2D.Create(256, 256, PixelInternalFormat.Rgba32ui, debugName: "Test_SurfaceLut");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);
        UploadMaterialPaletteAndSurfaceLut(materialPalette, surfaceLut);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneRelightVoxelDda.Miss.ParamsUBO");

        GL.UseProgram(program);
        using var sharedSurface = new SharedSurfaceInputFixture(program, occ, materialPalette);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        debugCounter.BindBase(bindingIndex: 0);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 6, materialPalette.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 7, surfaceLut.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        BindRelightParamsUbo(
            paramsUbo,
            tileSizeTexels: tileSize,
            tilesPerAxis: 1,
            tilesPerAtlas: 1,
            borderTexels: 0,
            frameIndex: 0,
            occResolution: occRes,
            texelsPerPagePerFrame: (uint)(tileSize * tileSize),
            raysPerTexel: 1u,
            maxDdaSteps: 8u,
            debugCountersEnabled: 0u,
            occOriginMinCell0X: 0,
            occOriginMinCell0Y: 0,
            occOriginMinCell0Z: 0,
            occRing0X: 0,
            occRing0Y: 0,
            occRing0Z: 0);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 2)");

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: 0, y: 0);

        Assert.Equal(0f, a);
        Assert.InRange(r, -1e-4f, 1e-4f);
        Assert.InRange(g, -1e-4f, 1e-4f);
        Assert.InRange(b, -1e-4f, 1e-4f);

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void Relight_TemporalAccumulation_IncrementsWeight_AndStaysFinite()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_relight_voxel_dda.csh", debugName: "Tests.RelightVoxelDda.TStep");
        int program = computeProgram.ProgramId;

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 32;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);
        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, packed);

        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        using var materialPalette = Texture2D.Create(16384, 1, PixelInternalFormat.Rgba32ui, debugName: "Test_MaterialPalette");
        using var surfaceLut = Texture2D.Create(256, 256, PixelInternalFormat.Rgba32ui, debugName: "Test_SurfaceLut");
        UploadMaterialPaletteAndSurfaceLut(materialPalette, surfaceLut);

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
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneRelightVoxelDda.TemporalAccum.ParamsUBO");

        GL.UseProgram(program);
        using var sharedSurface = new SharedSurfaceInputFixture(program, occ, materialPalette);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        debugCounter.BindBase(bindingIndex: 0);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 7, surfaceLut.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        uint commonTexelsPerFrame = (uint)(tileSize * tileSize);
        const uint commonRaysPerTexel = 1u;
        const uint commonMaxDdaSteps = 16u;
        const uint commonDebugCountersEnabled = 0u;

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;

        // Frame 0
        BindRelightParamsUbo(
            paramsUbo,
            tileSizeTexels: tileSize,
            tilesPerAxis: 1,
            tilesPerAtlas: 1,
            borderTexels: 0,
            frameIndex: 0,
            occResolution: occRes,
            texelsPerPagePerFrame: commonTexelsPerFrame,
            raysPerTexel: commonRaysPerTexel,
            maxDdaSteps: commonMaxDdaSteps,
            debugCountersEnabled: commonDebugCountersEnabled,
            occOriginMinCell0X: 0,
            occOriginMinCell0Y: 0,
            occOriginMinCell0Z: 0,
            occRing0X: 0,
            occRing0Y: 0,
            occRing0Z: 0);
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 3)");

        // Frame 1
        BindRelightParamsUbo(
            paramsUbo,
            tileSizeTexels: tileSize,
            tilesPerAxis: 1,
            tilesPerAtlas: 1,
            borderTexels: 0,
            frameIndex: 1,
            occResolution: occRes,
            texelsPerPagePerFrame: commonTexelsPerFrame,
            raysPerTexel: commonRaysPerTexel,
            maxDdaSteps: commonMaxDdaSteps,
            debugCountersEnabled: commonDebugCountersEnabled,
            occOriginMinCell0X: 0,
            occOriginMinCell0Y: 0,
            occOriginMinCell0Z: 0,
            occRing0X: 0,
            occRing0Y: 0,
            occRing0Z: 0);
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 4)");

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.InRange(a, 1.99f, 2.01f);
        Assert.False(float.IsNaN(r) || float.IsNaN(g) || float.IsNaN(b));
        Assert.False(float.IsInfinity(r) || float.IsInfinity(g) || float.IsInfinity(b));

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void Relight_HalfspaceOccupancy_FrontFacingHemisphere_AlwaysHits()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_relight_voxel_dda.csh", debugName: "Tests.RelightVoxelDda.SelfHit");
        int program = computeProgram.ProgramId;

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

        using var materialPalette = Texture2D.Create(16384, 1, PixelInternalFormat.Rgba32ui, debugName: "Test_MaterialPalette");
        using var surfaceLut = Texture2D.Create(256, 256, PixelInternalFormat.Rgba32ui, debugName: "Test_SurfaceLut");
        UploadMaterialPaletteAndSurfaceLut(materialPalette, surfaceLut);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        FindSafeSeedWithLocalZForHalfspace(
            occRes,
            physicalPageId: 1u,
            virtualPageIndex: 0u,
            out uint patchId,
            out int localZ);

        int startZ = localZ + 1; // floor(localZ + 1.01)
        uint packedSolid = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);

        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillOccHalfspaceZ(occ.TextureId, occRes, thresholdZ: startZ, solidForZGreaterOrEqual: true, packedSolid: packedSolid);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);

        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);
        debugCounter.UploadZeros(counterCount: 4);

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneRelightVoxelDda.Halfspace.ParamsUBO");

        GL.UseProgram(program);
        using var sharedSurface = new SharedSurfaceInputFixture(program, occ, materialPalette);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        debugCounter.BindBase(bindingIndex: 0);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 6, materialPalette.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 7, surfaceLut.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        BindRelightParamsUbo(
            paramsUbo,
            tileSizeTexels: tileSize,
            tilesPerAxis: 1,
            tilesPerAtlas: 1,
            borderTexels: 0,
            frameIndex: 0,
            occResolution: occRes,
            texelsPerPagePerFrame: (uint)(tileSize * tileSize),
            raysPerTexel: 1u,
            maxDdaSteps: 8u,
            debugCountersEnabled: 1u,
            occOriginMinCell0X: 0,
            occOriginMinCell0Y: 0,
            occOriginMinCell0Z: 0,
            occRing0X: 0,
            occRing0Y: 0,
            occRing0Z: 0);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 5)");

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

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void Relight_OutOfBoundsStart_AddsNoSample_EvenIfRingHasSolidCell()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_relight_voxel_dda.csh", debugName: "Tests.RelightVoxelDda.SelfHit2");
        int program = computeProgram.ProgramId;

        const int tileSize = 8;
        const int atlasCount = 1;

        // occRes=1 ensures the traced origin starts outside the valid volume for +Z normal due to +0.51 offset.
        const int occRes = 1;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);
        using var occ = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        FillR32ui3D(occ.TextureId, occRes, occRes, occRes, packed);

        using var lightColorLut = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, debugName: "Test_LightColorLut");
        using var blockScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_BlockScalar");
        using var sunScalar = Texture2D.Create(33, 1, PixelInternalFormat.R16f, debugName: "Test_SunScalar");
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        using var materialPalette = Texture2D.Create(16384, 1, PixelInternalFormat.Rgba32ui, debugName: "Test_MaterialPalette");
        using var surfaceLut = Texture2D.Create(256, 256, PixelInternalFormat.Rgba32ui, debugName: "Test_SurfaceLut");
        UploadMaterialPaletteAndSurfaceLut(materialPalette, surfaceLut);

        using var irradiance = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_IrradianceAtlas");
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var debugCounter = CreateAtomicCounterBuffer(counterCount: 4);

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneRelightVoxelDda.OobStart.ParamsUBO");

        GL.UseProgram(program);
        using var sharedSurface = new SharedSurfaceInputFixture(program, occ, materialPalette);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        debugCounter.BindBase(bindingIndex: 0);

        BindSampler(TextureTarget.Texture2DArray, unit: 0, depthAtlas.TextureId);
        BindSampler(TextureTarget.Texture2DArray, unit: 1, materialAtlas.TextureId);
        BindSampler(TextureTarget.Texture3D, unit: 2, occ.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 3, lightColorLut.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 4, blockScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 5, sunScalar.TextureId);
        BindSampler(TextureTarget.Texture2D, unit: 7, surfaceLut.TextureId);

        GL.BindImageTexture(0, irradiance.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.Rgba16f);

        BindRelightParamsUbo(
            paramsUbo,
            tileSizeTexels: tileSize,
            tilesPerAxis: 1,
            tilesPerAtlas: 1,
            borderTexels: 0,
            frameIndex: 0,
            occResolution: occRes,
            texelsPerPagePerFrame: (uint)(tileSize * tileSize),
            raysPerTexel: 1u,
            maxDdaSteps: 8u,
            debugCountersEnabled: 0u,
            occOriginMinCell0X: 0,
            occOriginMinCell0Y: 0,
            occOriginMinCell0Z: 0,
            occRing0X: 0,
            occRing0Y: 0,
            occRing0Z: 0);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 6)");

        float[] outRgba = ReadTexImageRgba16f_2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount);
        (float r, float g, float b, float a) = SampleRgba(outRgba, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.Equal(0f, a);
        Assert.InRange(r, -1e-4f, 1e-4f);
        Assert.InRange(g, -1e-4f, 1e-4f);
        Assert.InRange(b, -1e-4f, 1e-4f);

        // Program disposed via ComputeProgram.
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

    private static void BindRelightParamsUbo(
        ObjectParamsUbo paramsUbo,
        int tileSizeTexels,
        int tilesPerAxis,
        int tilesPerAtlas,
        int borderTexels,
        int frameIndex,
        int occResolution,
        uint texelsPerPagePerFrame,
        uint raysPerTexel,
        uint maxDdaSteps,
        uint debugCountersEnabled,
        int occOriginMinCell0X,
        int occOriginMinCell0Y,
        int occOriginMinCell0Z,
        int occRing0X,
        int occRing0Y,
        int occRing0Z)
    {
        Span<byte> paramsBytes = stackalloc byte[RelightParamsUboSizeBytes];

        UboPacking.WriteUVec4(paramsBytes, byteOffset: 0, (uint)tileSizeTexels, (uint)tilesPerAxis, (uint)tilesPerAtlas, (uint)borderTexels);
        UboPacking.WriteUVec4(paramsBytes, byteOffset: 16, texelsPerPagePerFrame, raysPerTexel, maxDdaSteps, debugCountersEnabled);
        UboPacking.WriteIVec4(paramsBytes, byteOffset: 32, frameIndex, occResolution, 0, 0);
        UboPacking.WriteIVec4(paramsBytes, byteOffset: 48, occOriginMinCell0X, occOriginMinCell0Y, occOriginMinCell0Z, 0);
        UboPacking.WriteIVec4(paramsBytes, byteOffset: 64, occRing0X, occRing0Y, occRing0Z, 0);

        paramsUbo.UploadAndBind(paramsBytes);
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
            // Illuminate air as well: hit lighting samples the cell outside the solid boundary.
            uint v = solid ? packedSolid : packedSolid & 0x3ffffu;
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

    private static void BindSampler(TextureTarget target, int unit, int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(target, textureId);
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

    private static new void SetUniform(int program, string name, int value)
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

    private static void UploadMaterialPaletteAndSurfaceLut(Texture2D materialPalette, Texture2D surfaceLut)
    {
        // Populate only entry 1: all faces use surfaceId=1.
        uint sid = 1u;
        uint packed2 = sid | (sid << 16);
        uint[] pal = new uint[16384 * 4];
        pal[1 * 4 + 0] = packed2;
        pal[1 * 4 + 1] = packed2;
        pal[1 * 4 + 2] = packed2;
        pal[1 * 4 + 3] = 0u;
        materialPalette.UploadDataImmediate(pal);

        // SurfaceLut: set surfaceId=1 at (x=1,y=0) to white albedo, roughness=0.
        uint[] surf = new uint[256 * 256 * 4];
        int o = (0 * 256 + 1) * 4;
        surf[o + 0] = 255u;
        surf[o + 1] = 255u;
        surf[o + 2] = 255u;
        surf[o + 3] = 0u;
        surfaceLut.UploadDataImmediate(surf);
    }

    private static void FillMaterialNormalPlusZ(int textureId, int width, int height, int depth)
    {
        // MaterialAtlas packing: RG = oct-encoded normal, BA = 16-bit surfaceId.
        // +Z oct encodes to (0.5, 0.5) => (128,128). surfaceId=0.
        byte[] data = new byte[checked(width * height * depth * 4)];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i + 0] = 128;
            data[i + 1] = 128;
            data[i + 2] = 0;
            data[i + 3] = 0;
        }

        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static void FillMaterialNormalPlusZ16f(int textureId, int width, int height, int depth)
    {
        // Exact (0.5, 0.5) oct encoding so decode produces an exact +Z normal.
        float[] data = new float[checked(width * height * depth * 4)];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i + 0] = 0.5f;
            data[i + 1] = 0.5f;
            data[i + 2] = 0.0f;
            data[i + 3] = 0.0f;
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
