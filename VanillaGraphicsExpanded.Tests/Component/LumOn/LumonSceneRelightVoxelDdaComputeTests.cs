using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using System;
using System.IO;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;

using Xunit;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises lumon scene relight voxel dda compute behavior using the test harness.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumonSceneRelightVoxelDdaComputeTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context to verify voxel-DDA relighting results.</summary>
    public LumonSceneRelightVoxelDdaComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Hit and miss outcomes
    /// <summary>Verifies a DDA hit produces irradiance and increments accumulated weight.</summary>

    [Fact]
    public void Relight_HitProducesNonZeroIrradiance_AndWeightIncrements()
    {
        // Arrange one valid surface sample and a ray origin that intersects occupied voxels.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneRelightVoxelDdaComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;
        using var camera = TestFrameCamera.CreateIdentity(1,1);
        computeProgram.FrameInputs = camera;

        const int tileSize = 8;
        const int tilesPerAxis = 1;
        const int tilesPerAtlas = 1;
        const int atlasCount = 1;
        const int occRes = 32;

        // Inputs: depth/material atlases (sampled).
        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        // TraceScene occupancy: fill entire volume as solid so DDA hits deterministically.
        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);
        uint[] occupancy = Enumerable.Repeat(packed, checked(occRes * occRes * occRes)).ToArray();

        // LUTs: lightId=1 -> red; scalars map [0..32] -> i/32.
        using var lightColorLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_LightColorLut" }).RequireResource();
        using var blockScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_BlockScalar" }).RequireResource();
        using var sunScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SunScalar" }).RequireResource();
        using var surfaceLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 256), Height = System.Math.Max(1, 256), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SurfaceLut" }).RequireResource();
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f); // disable sun
        uint[] palette = CreateMaterialPaletteAndUploadSurfaceLut(surfaceLut);

        // Output irradiance atlas.
        using var irradiance = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_IrradianceAtlas" }).RequireResource();
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
        using var debugCounter = new ComponentAtomicCounters(counterCount: 4);

        using var sharedSurface = new SharedSurfaceInputFixture(occRes, occupancy, palette);
        computeProgram.BindSharedGeometry(sharedSurface.Scene);

        // SSBO binding matches shader: binding=0.
        computeProgram.BindRelightWorkSsbo(workSsbo);
        computeProgram.BindPatchMetaSsbo(patchMetaSsbo);
        computeProgram.BindDebugCounters(debugCounter.Buffer);

        // Samplers use layout(binding=N): bind textures to those units.
        computeProgram.DepthAtlas = depthAtlas;
        computeProgram.MaterialAtlas = materialAtlas;

        computeProgram.LightColorLut = lightColorLut;
        computeProgram.BlockLevelScalarLut = blockScalar;
        computeProgram.SunLevelScalarLut = sunScalar;

        computeProgram.SurfaceLut = surfaceLut;

        // Output image binding matches shader: layout(binding=0, rgba16f) image2DArray.
        computeProgram.BindIrradianceAtlasImage(irradiance);

        computeProgram.SetAtlasLayout((uint)tileSize, (uint)tilesPerAxis, (uint)tilesPerAtlas, (uint)0);
        computeProgram.SetRelightParams((uint)(tileSize * tileSize), 1u, 16u, 0u != 0);
        computeProgram.SetOccupancyMapping(0, 0, 0, 0, 0, 0, occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        // Act: dispatch relighting for the configured surface work item.
        computeProgram.Dispatch(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 1)");

        // Assert the hit contributes irradiance and increments its accumulation weight.
        var outRgba=irradiance;
        (float r, float g, float b, float a) = SampleRgba(outRgba, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.InRange(a, 0.99f, 1.01f);
        Assert.True(r > 0.001f || g > 0.001f || b > 0.001f, $"Expected non-zero irradiance, got rgb=({r},{g},{b})");

    }

    /// <summary>Verifies relight debug counters report misses for empty occupancy.</summary>

    [Fact]
    public void Relight_DebugCounters_EmptyOcc_ReportsAllMisses()
    {
        // Arrange empty occupancy and enable counters for the relight dispatch.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneRelightVoxelDdaComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;
        using var camera = TestFrameCamera.CreateIdentity(1,1);
        computeProgram.FrameInputs = camera;

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 32;

        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);
        uint[] occupancy = Enumerable.Repeat(0u, checked(occRes * occRes * occRes)).ToArray();

        using var lightColorLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_LightColorLut" }).RequireResource();
        using var blockScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_BlockScalar" }).RequireResource();
        using var sunScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SunScalar" }).RequireResource();
        using var surfaceLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 256), Height = System.Math.Max(1, 256), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SurfaceLut" }).RequireResource();
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);

        uint[] palette = CreateMaterialPaletteAndUploadSurfaceLut(surfaceLut);

        using var irradiance = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_IrradianceAtlas" }).RequireResource();
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        FindSafeSeedForOccRes(occRes, physicalPageId: 1u, virtualPageIndex: 0u, out uint patchId);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);

        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var debugCounter = new ComponentAtomicCounters(counterCount: 4);
        debugCounter.UploadZeros(counterCount: 4);

        using var sharedSurface = new SharedSurfaceInputFixture(occRes, occupancy, palette);
        computeProgram.BindSharedGeometry(sharedSurface.Scene);
        computeProgram.BindRelightWorkSsbo(workSsbo);
        computeProgram.BindPatchMetaSsbo(patchMetaSsbo);
        computeProgram.BindDebugCounters(debugCounter.Buffer);

        computeProgram.DepthAtlas = depthAtlas;
        computeProgram.MaterialAtlas = materialAtlas;

        computeProgram.LightColorLut = lightColorLut;
        computeProgram.BlockLevelScalarLut = blockScalar;
        computeProgram.SunLevelScalarLut = sunScalar;

        computeProgram.SurfaceLut = surfaceLut;

        computeProgram.SurfaceLut = surfaceLut;

        computeProgram.BindIrradianceAtlasImage(irradiance);

        computeProgram.SetAtlasLayout((uint)tileSize, (uint)1, (uint)1, (uint)0);
        computeProgram.SetRelightParams((uint)(tileSize * tileSize), 1u, 16u, 1u != 0);
        computeProgram.SetOccupancyMapping(0, 0, 0, 0, 0, 0, occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        // Act: trace the configured rays through empty occupancy.
        computeProgram.Dispatch(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.AtomicCounterBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (atomic counter)");

        // Assert every ray is counted as a miss, with no hits or out-of-bounds starts.
        uint[] counters = debugCounter.Read(counterCount: 4);
        uint expectedRays = (uint)(tileSize * tileSize);
        Assert.Equal(expectedRays, counters[0]); // rays
        Assert.Equal(0u, counters[1]);           // hits
        Assert.Equal(expectedRays, counters[2]); // misses
        Assert.Equal(0u, counters[3]);           // oob starts

    }

    /// <summary>Verifies empty occupancy leaves relit RGB black and adds no sample weight.</summary>

    [Fact]
    public void Relight_EmptyOcc_LeavesRgbBlack_WithoutAddingWeight()
    {
        // Arrange empty occupancy and a cleared irradiance atlas.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneRelightVoxelDdaComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;
        using var camera = TestFrameCamera.CreateIdentity(1,1);
        computeProgram.FrameInputs = camera;

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 16;

        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);
        uint[] occupancy = Enumerable.Repeat(0u, checked(occRes * occRes * occRes)).ToArray();

        using var lightColorLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_LightColorLut" }).RequireResource();
        using var blockScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_BlockScalar" }).RequireResource();
        using var sunScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SunScalar" }).RequireResource();
        using var surfaceLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 256), Height = System.Math.Max(1, 256), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SurfaceLut" }).RequireResource();
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);
        uint[] palette = CreateMaterialPaletteAndUploadSurfaceLut(surfaceLut);

        using var irradiance = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_IrradianceAtlas" }).RequireResource();
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);

        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var debugCounter = new ComponentAtomicCounters(counterCount: 4);
        using var sharedSurface = new SharedSurfaceInputFixture(occRes, occupancy, palette);
        computeProgram.BindSharedGeometry(sharedSurface.Scene);
        computeProgram.BindRelightWorkSsbo(workSsbo);
        computeProgram.BindPatchMetaSsbo(patchMetaSsbo);
        computeProgram.BindDebugCounters(debugCounter.Buffer);

        computeProgram.DepthAtlas = depthAtlas;
        computeProgram.MaterialAtlas = materialAtlas;

        computeProgram.LightColorLut = lightColorLut;
        computeProgram.BlockLevelScalarLut = blockScalar;
        computeProgram.SunLevelScalarLut = sunScalar;

        computeProgram.SurfaceLut = surfaceLut;

        computeProgram.BindIrradianceAtlasImage(irradiance);

        computeProgram.SetAtlasLayout((uint)tileSize, (uint)1, (uint)1, (uint)0);
        computeProgram.SetRelightParams((uint)(tileSize * tileSize), 1u, 8u, 0u != 0);
        computeProgram.SetOccupancyMapping(0, 0, 0, 0, 0, 0, occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        // Act: relight the page with rays that cannot hit occupied voxels.
        computeProgram.Dispatch(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 2)");

        // Assert misses leave both irradiance and accumulated weight unchanged.
        var outRgba=irradiance;
        (float r, float g, float b, float a) = SampleRgba(outRgba, layer: 0, x: 0, y: 0);

        Assert.Equal(0f, a);
        Assert.InRange(r, -1e-4f, 1e-4f);
        Assert.InRange(g, -1e-4f, 1e-4f);
        Assert.InRange(b, -1e-4f, 1e-4f);

    }

    #endregion

    #region Temporal accumulation and boundary outcomes
    /// <summary>Verifies temporal relight accumulation increases weight while remaining finite.</summary>

    [Fact]
    public void Relight_TemporalAccumulation_IncrementsWeight_AndStaysFinite()
    {
        // Arrange one relight page and identical inputs for two consecutive frame submissions.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneRelightVoxelDdaComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;
        using var camera = TestFrameCamera.CreateIdentity(1,1);
        computeProgram.FrameInputs = camera;

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 32;

        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);
        uint[] occupancy = Enumerable.Repeat(packed, checked(occRes * occRes * occRes)).ToArray();

        using var lightColorLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_LightColorLut" }).RequireResource();
        using var blockScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_BlockScalar" }).RequireResource();
        using var sunScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SunScalar" }).RequireResource();
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);
        using var surfaceLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 256), Height = System.Math.Max(1, 256), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SurfaceLut" }).RequireResource();
        uint[] palette = CreateMaterialPaletteAndUploadSurfaceLut(surfaceLut);

        using var irradiance = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_IrradianceAtlas" }).RequireResource();
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
        using var debugCounter = new ComponentAtomicCounters(counterCount: 4);
        using var sharedSurface = new SharedSurfaceInputFixture(occRes, occupancy, palette);
        computeProgram.BindSharedGeometry(sharedSurface.Scene);
        computeProgram.BindRelightWorkSsbo(workSsbo);
        computeProgram.BindPatchMetaSsbo(patchMetaSsbo);
        computeProgram.BindDebugCounters(debugCounter.Buffer);

        computeProgram.DepthAtlas = depthAtlas;
        computeProgram.MaterialAtlas = materialAtlas;

        computeProgram.LightColorLut = lightColorLut;
        computeProgram.BlockLevelScalarLut = blockScalar;
        computeProgram.SunLevelScalarLut = sunScalar;
        computeProgram.SurfaceLut = surfaceLut;

        computeProgram.BindIrradianceAtlasImage(irradiance);

        uint commonTexelsPerFrame = (uint)(tileSize * tileSize);
        const uint commonRaysPerTexel = 1u;
        const uint commonMaxDdaSteps = 16u;
        const uint commonDebugCountersEnabled = 0u;

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;

        // Act: submit the same relight work in two consecutive frames.
        // Frame 0
        computeProgram.SetAtlasLayout((uint)tileSize, (uint)1, (uint)1, (uint)0);
        computeProgram.SetRelightParams(commonTexelsPerFrame, commonRaysPerTexel, commonMaxDdaSteps, commonDebugCountersEnabled != 0);
        computeProgram.SetOccupancyMapping(0, 0, 0, 0, 0, 0, occRes);
        computeProgram.Dispatch(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 3)");

        // Frame 1
        computeProgram.SetAtlasLayout((uint)tileSize, (uint)1, (uint)1, (uint)0);
        using var nextCamera = TestFrameCamera.CreateIdentity(1,1,frameIndex:1);
        computeProgram.FrameInputs = nextCamera;
        computeProgram.SetRelightParams(commonTexelsPerFrame, commonRaysPerTexel, commonMaxDdaSteps, commonDebugCountersEnabled != 0);
        computeProgram.SetOccupancyMapping(0, 0, 0, 0, 0, 0, occRes);
        computeProgram.Dispatch(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 4)");

        // Assert the two submissions accumulate two finite samples at the observed texel.
        var outRgba=irradiance;
        (float r, float g, float b, float a) = SampleRgba(outRgba, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.InRange(a, 1.99f, 2.01f);
        Assert.False(float.IsNaN(r) || float.IsNaN(g) || float.IsNaN(b));
        Assert.False(float.IsInfinity(r) || float.IsInfinity(g) || float.IsInfinity(b));

    }

    /// <summary>Verifies front-facing rays hit a half-space occupancy field.</summary>

    [Fact]
    public void Relight_HalfspaceOccupancy_FrontFacingHemisphere_AlwaysHits()
    {
        // Arrange occupied halfspace and counters to observe front-facing hemisphere rays.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneRelightVoxelDdaComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;
        using var camera = TestFrameCamera.CreateIdentity(1,1);
        computeProgram.FrameInputs = camera;

        const int tileSize = 8;
        const int atlasCount = 1;
        const int occRes = 32;

        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        using var lightColorLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_LightColorLut" }).RequireResource();
        using var blockScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_BlockScalar" }).RequireResource();
        using var sunScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SunScalar" }).RequireResource();
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);
        using var surfaceLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 256), Height = System.Math.Max(1, 256), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SurfaceLut" }).RequireResource();
        uint[] palette = CreateMaterialPaletteAndUploadSurfaceLut(surfaceLut);

        using var irradiance = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_IrradianceAtlas" }).RequireResource();
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        FindSafeSeedWithLocalZForHalfspace(
            occRes,
            physicalPageId: 1u,
            virtualPageIndex: 0u,
            out uint patchId,
            out int localZ);

        int startZ = localZ + 1; // floor(localZ + 1.01)
        uint packedSolid = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);
        uint[] occupancy = CreateOccHalfspaceZ(occRes, thresholdZ: startZ, solidForZGreaterOrEqual: true, packedSolid: packedSolid);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: patchId, virtualPageIndex: 0u);

        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var debugCounter = new ComponentAtomicCounters(counterCount: 4);
        debugCounter.UploadZeros(counterCount: 4);

        using var sharedSurface = new SharedSurfaceInputFixture(occRes, occupancy, palette);
        computeProgram.BindSharedGeometry(sharedSurface.Scene);
        computeProgram.BindRelightWorkSsbo(workSsbo);
        computeProgram.BindPatchMetaSsbo(patchMetaSsbo);
        computeProgram.BindDebugCounters(debugCounter.Buffer);

        computeProgram.DepthAtlas = depthAtlas;
        computeProgram.MaterialAtlas = materialAtlas;

        computeProgram.LightColorLut = lightColorLut;
        computeProgram.BlockLevelScalarLut = blockScalar;
        computeProgram.SunLevelScalarLut = sunScalar;

        computeProgram.SurfaceLut = surfaceLut;

        computeProgram.BindIrradianceAtlasImage(irradiance);

        computeProgram.SetAtlasLayout((uint)tileSize, (uint)1, (uint)1, (uint)0);
        computeProgram.SetRelightParams((uint)(tileSize * tileSize), 1u, 8u, 1u != 0);
        computeProgram.SetOccupancyMapping(0, 0, 0, 0, 0, 0, occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        // Act: trace the hemisphere sample set against the occupied halfspace.
        computeProgram.Dispatch(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 5)");

        // Assert every ray hits, contributes irradiance, and increments sample weight.
        uint[] counters = debugCounter.Read(counterCount: 4);
        uint expectedRays = (uint)(tileSize * tileSize);
        Assert.Equal(expectedRays, counters[0]); // rays
        Assert.Equal(expectedRays, counters[1]); // hits
        Assert.Equal(0u, counters[2]);           // misses
        Assert.Equal(0u, counters[3]);           // oob starts

        var outRgba=irradiance;
        (float r, float g, float b, float a) = SampleRgba(outRgba, layer: 0, x: tileSize / 2, y: tileSize / 2);
        Assert.InRange(a, 0.99f, 1.01f);
        Assert.True(r > 1e-3f || g > 1e-3f || b > 1e-3f, "Expected non-zero irradiance from halfspace hits.");

    }

    /// <summary>Verifies an out-of-bounds DDA start adds no sample despite nearby solid occupancy.</summary>

    [Fact]
    public void Relight_OutOfBoundsStart_AddsNoSample_EvenIfRingHasSolidCell()
    {
        // Arrange a solid occupancy ring while placing the relight origin outside the valid volume.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneRelightVoxelDdaComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;
        using var camera = TestFrameCamera.CreateIdentity(1,1);
        computeProgram.FrameInputs = camera;

        const int tileSize = 8;
        const int atlasCount = 1;

        // occRes=1 ensures the traced origin starts outside the valid volume for +Z normal due to +0.51 offset.
        const int occRes = 1;

        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();
        FillR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 0f);
        FillMaterialNormalPlusZ(materialAtlas.TextureId, tileSize, tileSize, atlasCount);

        uint packed = LumonSceneOccupancyPacking.PackClamped(blockLevel: 32, sunLevel: 0, lightId: 1, materialPaletteIndex: 1);
        uint[] occupancy = Enumerable.Repeat(packed, checked(occRes * occRes * occRes)).ToArray();

        using var lightColorLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_LightColorLut" }).RequireResource();
        using var blockScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_BlockScalar" }).RequireResource();
        using var sunScalar = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16f, Width = System.Math.Max(1, 33), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SunScalar" }).RequireResource();
        UploadLightColorLut(lightColorLut, redId: 1);
        UploadLinearScalarLut(blockScalar, scale: 1f);
        UploadLinearScalarLut(sunScalar, scale: 0f);
        using var surfaceLut = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 256), Height = System.Math.Max(1, 256), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_SurfaceLut" }).RequireResource();
        uint[] palette = CreateMaterialPaletteAndUploadSurfaceLut(surfaceLut);

        using var irradiance = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_IrradianceAtlas" }).RequireResource();
        FillRgba16f2DArray(irradiance.TextureId, tileSize, tileSize, atlasCount, r: 0f, g: 0f, b: 0f, a: 0f);

        Span<LumonSceneRelightWorkGpu> work = stackalloc LumonSceneRelightWorkGpu[1];
        work[0] = new LumonSceneRelightWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);

        using var workSsbo = CreateSsbo<LumonSceneRelightWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var debugCounter = new ComponentAtomicCounters(counterCount: 4);
        using var sharedSurface = new SharedSurfaceInputFixture(occRes, occupancy, palette);
        computeProgram.BindSharedGeometry(sharedSurface.Scene);
        computeProgram.BindRelightWorkSsbo(workSsbo);
        computeProgram.BindPatchMetaSsbo(patchMetaSsbo);
        computeProgram.BindDebugCounters(debugCounter.Buffer);

        computeProgram.DepthAtlas = depthAtlas;
        computeProgram.MaterialAtlas = materialAtlas;

        computeProgram.LightColorLut = lightColorLut;
        computeProgram.BlockLevelScalarLut = blockScalar;
        computeProgram.SunLevelScalarLut = sunScalar;
        computeProgram.SurfaceLut = surfaceLut;

        computeProgram.BindIrradianceAtlasImage(irradiance);

        computeProgram.SetAtlasLayout((uint)tileSize, (uint)1, (uint)1, (uint)0);
        computeProgram.SetRelightParams((uint)(tileSize * tileSize), 1u, 8u, 0u != 0);
        computeProgram.SetOccupancyMapping(0, 0, 0, 0, 0, 0, occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        // Act: dispatch relighting from the out-of-bounds start position.
        computeProgram.Dispatch(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("RelightVoxelDda dispatch (case 6)");

        // Assert the ring cannot turn an invalid start into a sample or weighted result.
        var outRgba=irradiance;
        (float r, float g, float b, float a) = SampleRgba(outRgba, layer: 0, x: tileSize / 2, y: tileSize / 2);

        Assert.Equal(0f, a);
        Assert.InRange(r, -1e-4f, 1e-4f);
        Assert.InRange(g, -1e-4f, 1e-4f);
        Assert.InRange(b, -1e-4f, 1e-4f);

    }

    #endregion
    #endregion

    #region Private
    #region Deterministic ray-start and occupancy setup
    /// <summary>Finds a deterministic patch seed that addresses the requested occupancy page.</summary>
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

    /// <summary>Finds a deterministic patch seed and local depth coordinate for the half-space case.</summary>
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

    /// <summary>Authors the full halfspace while retaining illuminated boundary air cells.</summary>
    private static uint[] CreateOccHalfspaceZ(int res, int thresholdZ, bool solidForZGreaterOrEqual, uint packedSolid)
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

        return data;
    }

    #endregion

    #region Shader inputs and lookup tables
    /// <summary>Allocates and uploads a typed span into a shader-storage buffer.</summary>
    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = name }).RequireResource();
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
    }

    /// <summary>Uploads the light-color lookup values required by the compute shader.</summary>
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

    /// <summary>Uploads a linear scalar lookup table with the requested scale.</summary>
    private static void UploadLinearScalarLut(Texture2D lut, float scale)
    {
        float[] data = new float[33];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (i / 32f) * scale;
        }
        lut.UploadDataImmediate(data);
    }

    /// <summary>Authors face identities and uploads the actual surface lookup consumed by relighting.</summary>
    private static uint[] CreateMaterialPaletteAndUploadSurfaceLut(Texture2D surfaceLut)
    {
        // Populate only entry 1: all faces use surfaceId=1.
        uint sid = 1u;
        uint packed2 = sid | (sid << 16);
        uint[] pal = new uint[16384 * 4];
        pal[1 * 4 + 0] = packed2;
        pal[1 * 4 + 1] = packed2;
        pal[1 * 4 + 2] = packed2;
        pal[1 * 4 + 3] = 0u;


        // SurfaceLut: set surfaceId=1 at (x=1,y=0) to white albedo, roughness=0.
        uint[] surf = new uint[256 * 256 * 4];
        int o = (0 * 256 + 1) * 4;
        surf[o + 0] = 255u;
        surf[o + 1] = 255u;
        surf[o + 2] = 255u;
        surf[o + 3] = 0u;
        surfaceLut.UploadDataImmediate(surf);
        return pal;
    }

    #endregion

    #region Atlas initialization and observations
    /// <summary>Fills a texture array with positive-Z material normals.</summary>
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

    /// <summary>Fills a 16-bit-float texture array with positive-Z material normals.</summary>
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

    /// <summary>Fills every layer of a 2D-array texture with one scalar value.</summary>
    private static void FillR16f2DArray(int textureId, int width, int height, int depth, float value)
    {
        float[] data = new float[checked(width * height * depth)];
        Array.Fill(data, value);

        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Red, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    /// <summary>Fills every layer of a 2D-array texture with one RGBA16F value.</summary>
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

    /// <summary>Decodes only the originally nominated irradiance tuple, retaining its explicit atlas layer.</summary>
    private static (float R, float G, float B, float A) SampleRgba(GpuTexture image, int layer, int x, int y)
    {
        var rgba=GpuLocalRead.Tuple(image,x,y,layer);
        int idx=0;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
    }

    #endregion
    #endregion

}
