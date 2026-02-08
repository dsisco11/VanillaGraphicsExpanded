using System;
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
public sealed class LumonSceneVoxelCaptureComputeTests : RenderTestBase
{
    public LumonSceneVoxelCaptureComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void CaptureVoxel_SingleWorkItem_WritesDepthZero_AndExpectedMaterial()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_capture_voxel.csh", debugName: "Tests.LumonSceneVoxelCapture.SingleWorkItem");
        int program = computeProgram.ProgramId;

        const int tileSize = 16;
        const int tilesPerAxis = 1;
        const int tilesPerAtlas = 1;
        const int atlasCount = 1;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");

        const int occRes = 32;
        using var occL0 = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        using var materialPalette = Texture2D.Create(width: 64, height: 1, format: PixelInternalFormat.Rgba32ui, filter: TextureFilterMode.Nearest, debugName: "Test_MaterialPalette");

        ClearR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, tileSize, tileSize, atlasCount, r: 0, g: 0, b: 0, a: 0);

        // Fill occupancy with a constant material palette index (5) and palette entry (5) with a known surface id.
        uint occPacked = LumonSceneOccupancyPacking.Pack(blockLevel: 0, sunLevel: 0, lightId: 0, materialPaletteIndex: 5);
        uint[] occ = new uint[occRes * occRes * occRes];
        Array.Fill(occ, occPacked);
        occL0.UploadDataImmediate(occ, x: 0, y: 0, z: 0, regionWidth: occRes, regionHeight: occRes, regionDepth: occRes, mipLevel: 0);

        uint[] pal = new uint[64 * 4];
        uint sid = 9u;
        uint packed2 = sid | (sid << 16);
        pal[5 * 4 + 0] = packed2;
        pal[5 * 4 + 1] = packed2;
        pal[5 * 4 + 2] = packed2;
        pal[5 * 4 + 3] = 0u;
        materialPalette.UploadDataImmediate(pal);

        // physicalPageId=1 maps to tile (0,0) in atlas layer 0.
        Span<LumonSceneCaptureWorkGpu> work = stackalloc LumonSceneCaptureWorkGpu[1];
        work[0] = new LumonSceneCaptureWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var slotInfoSsbo = CreateSsbo<int>("Test_ChunkSlotInfoSSBO", new int[4]);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        slotInfoSsbo.BindBase(bindingIndex: 2);

        // Bind output images to match shader layout(binding=...).
        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        BindSampler3D(unit: 2, occL0.TextureId);
        BindSampler2D(unit: 3, materialPalette.TextureId);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", (uint)tilesPerAxis);
        SetUniform(program, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
        _ = TrySetUniform(program, "vge_borderTexels", 0u);
        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform1i(program, "vge_occResolution", occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("VoxelCapture dispatch (single)");

        float[] depth = ReadTexImageR32f_2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount);
        (float min, float max) = MinMax(depth);
        Assert.InRange(min, -0.02f, 0.02f);
        Assert.InRange(max, -0.02f, 0.02f);

        byte[] material = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, tileSize, tileSize, atlasCount);
        (byte r, byte g, byte b, byte a) = ReadRgbaAt(material, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);
        Assert.NotEqual((byte)0, r); // oct-normal x
        Assert.NotEqual((byte)0, g); // oct-normal y
        Assert.Equal((byte)9, b);    // surfaceId low byte
        Assert.Equal((byte)0, a);    // surfaceId high byte

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void CaptureVoxel_MultiChunkSlots_WritesPatchMetadataOriginFromChunkSlotInfo()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_capture_voxel.csh", debugName: "Tests.LumonSceneVoxelCapture.MultiChunkSlots");
        int program = computeProgram.ProgramId;

        const int tileSize = 16;
        const int tilesPerAxis = 2;
        const int tilesPerAtlas = tilesPerAxis * tilesPerAxis;
        const int atlasCount = 1;

        int w = tileSize * tilesPerAxis;
        int h = tileSize * tilesPerAxis;

        using var depthAtlas = Texture3D.Create(w, h, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(w, h, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        ClearR16f2DArray(depthAtlas.TextureId, w, h, atlasCount, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, w, h, atlasCount, r: 0, g: 0, b: 0, a: 0);

        // Two pages, same patchId, different chunkSlots.
        Span<LumonSceneCaptureWorkGpu> work = stackalloc LumonSceneCaptureWorkGpu[2];
        work[0] = new LumonSceneCaptureWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);
        work[1] = new LumonSceneCaptureWorkGpu(physicalPageId: 2u, chunkSlot: 1u, patchId: 1u, virtualPageIndex: 0u);

        using var workSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[3]);

        // Slot 0 origin=(0,0,0); slot 1 origin=(32,0,0). Generation=0.
        using var slotInfoSsbo = CreateSsbo<int>("Test_ChunkSlotInfoSSBO", new[] { 0, 0, 0, 0, 32, 0, 0, 0 });

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        slotInfoSsbo.BindBase(bindingIndex: 2);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", (uint)tilesPerAxis);
        SetUniform(program, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
        _ = TrySetUniform(program, "vge_borderTexels", 0u);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 2);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("VoxelCapture dispatch (multi-slice)");

        using var mapped = patchMetaSsbo.MapRange<LumonScenePatchMetadataGpu>(dstOffsetBytes: 0, elementCount: 3, access: MapBufferAccessMask.MapReadBit);
        Assert.True(mapped.IsMapped);

        LumonScenePatchMetadataGpu m0 = mapped.Span[1];
        LumonScenePatchMetadataGpu m1 = mapped.Span[2];

        Assert.Equal(0u, m0.ChunkSlot);
        Assert.Equal(1u, m1.ChunkSlot);
        Assert.Equal(1u, m0.PatchId);
        Assert.Equal(1u, m1.PatchId);

        // patchId=1 => +X face, plane=0, patchU=0, patchV=0 => origin = chunkOrigin + (1,0,0).
        Assert.InRange(m0.OriginWS.X, 0.99f, 1.01f);
        Assert.InRange(m0.OriginWS.Y, -0.01f, 0.01f);
        Assert.InRange(m0.OriginWS.Z, -0.01f, 0.01f);

        Assert.InRange(m1.OriginWS.X, 32.99f, 33.01f);
        Assert.InRange(m1.OriginWS.Y, -0.01f, 0.01f);
        Assert.InRange(m1.OriginWS.Z, -0.01f, 0.01f);

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void CaptureVoxel_MultiplePages_AddressingDoesNotOverlap_AndRespectsAtlasLayer()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_capture_voxel.csh", debugName: "Tests.LumonSceneVoxelCapture.MultipleAtlases");
        int program = computeProgram.ProgramId;

        const int tileSize = 8;
        const int tilesPerAxis = 2;
        const int tilesPerAtlas = tilesPerAxis * tilesPerAxis; // 4
        const int atlasCount = 2;

        int w = tileSize * tilesPerAxis;
        int h = tileSize * tilesPerAxis;

        using var depthAtlas = Texture3D.Create(w, h, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(w, h, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        const int occRes = 32;
        using var occL0 = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        using var materialPalette = Texture2D.Create(width: 64, height: 1, format: PixelInternalFormat.Rgba32ui, filter: TextureFilterMode.Nearest, debugName: "Test_MaterialPalette");

        ClearR16f2DArray(depthAtlas.TextureId, w, h, atlasCount, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, w, h, atlasCount, r: 0, g: 0, b: 0, a: 0);

        uint occPacked = LumonSceneOccupancyPacking.Pack(blockLevel: 0, sunLevel: 0, lightId: 0, materialPaletteIndex: 5);
        uint[] occ = new uint[occRes * occRes * occRes];
        Array.Fill(occ, occPacked);
        occL0.UploadDataImmediate(occ, x: 0, y: 0, z: 0, regionWidth: occRes, regionHeight: occRes, regionDepth: occRes, mipLevel: 0);

        uint[] pal = new uint[64 * 4];
        uint sid = 9u;
        uint packed2 = sid | (sid << 16);
        pal[5 * 4 + 0] = packed2;
        pal[5 * 4 + 1] = packed2;
        pal[5 * 4 + 2] = packed2;
        pal[5 * 4 + 3] = 0u;
        materialPalette.UploadDataImmediate(pal);

        // Work:
        // - pid=1 -> pageIndex=0 -> atlas0 tile(0,0)
        // - pid=4 -> pageIndex=3 -> atlas0 tile(1,1)
        // - pid=5 -> pageIndex=4 -> atlas1 tile(0,0)
        Span<LumonSceneCaptureWorkGpu> work = stackalloc LumonSceneCaptureWorkGpu[3];
        work[0] = new LumonSceneCaptureWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u); // +X
        work[1] = new LumonSceneCaptureWorkGpu(physicalPageId: 4u, chunkSlot: 0u, patchId: 2u, virtualPageIndex: 0u); // -X
        work[2] = new LumonSceneCaptureWorkGpu(physicalPageId: 5u, chunkSlot: 0u, patchId: 3u, virtualPageIndex: 0u); // +Y
        using var workSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[6]);
        using var slotInfoSsbo = CreateSsbo<int>("Test_ChunkSlotInfoSSBO", new int[4]);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        slotInfoSsbo.BindBase(bindingIndex: 2);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        BindSampler3D(unit: 2, occL0.TextureId);
        BindSampler2D(unit: 3, materialPalette.TextureId);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", (uint)tilesPerAxis);
        SetUniform(program, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
        _ = TrySetUniform(program, "vge_borderTexels", 0u);
        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform1i(program, "vge_occResolution", occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 3);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("VoxelCapture dispatch (3D)");

        // Tile centers:
        // atlas0 tile(0,0): center at (tileSize/2, tileSize/2)
        // atlas0 tile(1,1): center at (tileSize + tileSize/2, tileSize + tileSize/2)
        // atlas1 tile(0,0): center at (tileSize/2, tileSize/2) in layer 1
        byte[] material = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, w, h, atlasCount);

        (byte _, byte _, byte b00, byte a00) = ReadRgbaAt(material, w, h, layer: 0, x: tileSize / 2, y: tileSize / 2);
        Assert.Equal((byte)9, b00);
        Assert.Equal((byte)0, a00);

        (byte _, byte _, byte b11, byte a11) = ReadRgbaAt(material, w, h, layer: 0, x: tileSize + tileSize / 2, y: tileSize + tileSize / 2);
        Assert.Equal((byte)9, b11);
        Assert.Equal((byte)0, a11);

        (byte _, byte _, byte bLayer1, byte aLayer1) = ReadRgbaAt(material, w, h, layer: 1, x: tileSize / 2, y: tileSize / 2);
        Assert.Equal((byte)9, bLayer1);
        Assert.Equal((byte)0, aLayer1);

        // Unwritten tile atlas0 tile(1,0) center should remain surfaceId=0 and depth=1.
        (byte _, byte _, byte bUnwritten, byte aUnwritten) = ReadRgbaAt(material, w, h, layer: 0, x: tileSize + tileSize / 2, y: tileSize / 2);
        Assert.Equal((byte)0, bUnwritten);
        Assert.Equal((byte)0, aUnwritten);

        float[] depth = ReadTexImageR32f_2DArray(depthAtlas.TextureId, w, h, atlasCount);
        float dUnwritten = depth[LinearIndex(w, h, layer: 0, x: tileSize + tileSize / 2, y: tileSize / 2)];
        Assert.InRange(dUnwritten, 0.98f, 1.02f);

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void CaptureVoxel_BorderTexelsNonZero_DoesNotBreakWrites()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_capture_voxel.csh", debugName: "Tests.LumonSceneVoxelCapture.LayeredWrites");
        int program = computeProgram.ProgramId;

        const int tileSize = 16;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");
        const int occRes = 32;
        using var occL0 = Texture3D.Create(occRes, occRes, occRes, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D, "Test_OccL0");
        using var materialPalette = Texture2D.Create(width: 64, height: 1, format: PixelInternalFormat.Rgba32ui, filter: TextureFilterMode.Nearest, debugName: "Test_MaterialPalette");

        ClearR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, depth: 1, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, tileSize, tileSize, depth: 1, r: 0, g: 0, b: 0, a: 0);

        uint occPacked = LumonSceneOccupancyPacking.Pack(blockLevel: 0, sunLevel: 0, lightId: 0, materialPaletteIndex: 5);
        uint[] occ = new uint[occRes * occRes * occRes];
        Array.Fill(occ, occPacked);
        occL0.UploadDataImmediate(occ, x: 0, y: 0, z: 0, regionWidth: occRes, regionHeight: occRes, regionDepth: occRes, mipLevel: 0);

        uint[] pal = new uint[64 * 4];
        uint sid = 9u;
        uint packed2 = sid | (sid << 16);
        pal[5 * 4 + 0] = packed2;
        pal[5 * 4 + 1] = packed2;
        pal[5 * 4 + 2] = packed2;
        pal[5 * 4 + 3] = 0u;
        materialPalette.UploadDataImmediate(pal);

        Span<LumonSceneCaptureWorkGpu> work = stackalloc LumonSceneCaptureWorkGpu[1];
        work[0] = new LumonSceneCaptureWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 6u, virtualPageIndex: 0u); // -Z
        using var workSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_WorkSSBO", work);
        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[2]);
        using var slotInfoSsbo = CreateSsbo<int>("Test_ChunkSlotInfoSSBO", new int[4]);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        patchMetaSsbo.BindBase(bindingIndex: 1);
        slotInfoSsbo.BindBase(bindingIndex: 2);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        BindSampler3D(unit: 2, occL0.TextureId);
        BindSampler2D(unit: 3, materialPalette.TextureId);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", 1u);
        SetUniform(program, "vge_tilesPerAtlas", 1u);
        _ = TrySetUniform(program, "vge_borderTexels", 2u);
        SetUniform3i(program, "vge_occOriginMinCell0", 0, 0, 0);
        SetUniform3i(program, "vge_occRing0", 0, 0, 0);
        SetUniform1i(program, "vge_occResolution", occRes);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("VoxelCapture dispatch (final)");

        byte[] material = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, tileSize, tileSize, depth: 1);
        (byte r, byte g, byte b, byte a) = ReadRgbaAt(material, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);
        Assert.NotEqual((byte)0, r);
        Assert.NotEqual((byte)0, g);
        Assert.Equal((byte)9, b);
        Assert.Equal((byte)0, a);

        // Program disposed via ComputeProgram.
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

    private static bool TrySetUniform(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0 && ComputeProgram.TryGetExplicitUniformLocation(program, name, out int explicitLoc))
        {
            loc = explicitLoc;
        }

        if (loc < 0)
        {
            return false;
        }
        GL.Uniform1(loc, value);
        return true;
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
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

    private static float[] ReadTexImageR32f_2DArray(int textureId, int width, int height, int depth)
    {
        float[] data = new float[checked(width * height * depth)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.GetTexImage(TextureTarget.Texture2DArray, level: 0, PixelFormat.Red, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
        return data;
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

    private static int LinearIndex(int width, int height, int layer, int x, int y)
        => ((layer * height + y) * width) + x;

    private static (byte R, byte G, byte B, byte A) ReadRgbaAt(byte[] rgba, int width, int height, int layer, int x, int y)
    {
        int idx = ((layer * height + y) * width + x) * 4;
        byte r = rgba[idx + 0];
        byte g = rgba[idx + 1];
        byte b = rgba[idx + 2];
        byte a = rgba[idx + 3];

        return (r, g, b, a);
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

    private static (float Min, float Max) MinMax(ReadOnlySpan<float> v)
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < v.Length; i++)
        {
            float f = v[i];
            min = Math.Min(min, f);
            max = Math.Max(max, f);
        }
        return (min, max);
    }
}
