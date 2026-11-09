using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;

using Xunit;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises lumon scene material atlas multi frame population behavior using the test harness.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumonSceneMaterialAtlasMultiFramePopulationTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context to verify atlas population across frame submissions.</summary>
    public LumonSceneMaterialAtlasMultiFramePopulationTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Verifies repeated capture frames populate every allocated atlas tile for one chunk.</summary>

    [Fact]
    public void MaterialAtlas_MultiFrame_SingleChunk_EventuallyPopulatesAllAllocatedTiles()
    {
        // Arrange page feedback and atlas capture inputs; the no-op writer excludes page-table publication.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var markShaderOwner, out string markShaderLog), markShaderLog);
        using var markShader = markShaderOwner!;
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactShaderOwner, out string compactShaderLog), compactShaderLog);
        using var compactShader = compactShaderOwner!;
        Assert.True(LumonSceneCaptureVoxelComputeShader.TryCreate(assets.Api, out var captureComputeProgramOwner, out string captureComputeProgramLog), captureComputeProgramLog);
        using var captureComputeProgram = captureComputeProgramOwner!;

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

        using var patchIdGBuffer = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, gW), Height = System.Math.Max(1, gH), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PatchIdGBuffer" }).RequireResource();

        const int chunkSlotCount = 1;
        using var usageStamp = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageUsageStamp" }).RequireResource();
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageTableMip0" }).RequireResource();
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var genTex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = System.Math.Max(1, chunkSlotCount), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_ChunkSlotGeneration" }).RequireResource();
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
        using var markCounters = CreateAtomicCounterBuffer(counterCount: 3);

        // GPU atlas outputs (we only validate that tiles are written).
        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, atlasW), Height = System.Math.Max(1, atlasH), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, atlasW), Height = System.Math.Max(1, atlasH), Depth = System.Math.Max(1, atlasCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();
        const int occRes = 32;

        FillR16f2DArray(depthAtlas.TextureId, atlasW, atlasH, atlasCount, value: 0f);
        FillRgba8_2DArray(materialAtlas.TextureId, atlasW, atlasH, atlasCount, r: 0, g: 0, b: 0, a: 0);

        uint occPacked = LumonSceneOccupancyPacking.Pack(blockLevel: 0, sunLevel: 0, lightId: 0, materialPaletteIndex: 1);
        uint[] occ = new uint[occRes * occRes * occRes];
        Array.Fill(occ, occPacked);

        uint[] pal = new uint[64 * 4];
        uint sid = 9u;
        uint packed2 = sid | (sid << 16);
        pal[1 * 4 + 0] = packed2;
        pal[1 * 4 + 1] = packed2;
        pal[1 * 4 + 2] = packed2;
        pal[1 * 4 + 3] = 0u;

        using var patchMetaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_PatchMetaSSBO", new LumonScenePatchMetadataGpu[desiredPages + 1]);
        using var slotInfoSsbo = CreateSsbo<int>("Test_ChunkSlotInfoSSBO", new int[4]);

        var captureOut = new LumonSceneCaptureWorkGpu[desiredPages];
        var relightOut = new LumonSceneRelightWorkGpu[desiredPages];
        int recaptureCursor = 0;

        // Act: repeat mark, compact, allocate, and capture until all requested pages are resident.
        for (uint frameStamp = 1; frameStamp <= maxFrames && virtualToPhysical.Count < desiredPages; frameStamp++)
        {
            // Pass A: mark pages.
            markShader.BindDebugCounters(markCounters);
            markShader.PatchIdG = patchIdGBuffer;
            markShader.ChunkSlotGenerationTex = genTex;
            markShader.FrameStamp = frameStamp;
            markShader.BindPageUsageStampImage(usageStamp, access: ImageAccess.ReadWrite);
            markShader.Dispatch((gW + 7) / 8, (gH + 7) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            GpuTestFence.WaitForGpuOrSkip("MaterialAtlas multi-frame mark pass dispatch");

            ResetAtomicCounter(pageRequestCounter, counterIndex: 0);

            // Pass B: compact stamps -> bounded request list.
            compactShader.BindRequestCounter(pageRequestCounter);
            compactShader.BindRequestsSsbo(pageRequests);
            compactShader.PageUsageStamp = usageStamp;
            compactShader.PageTableMip0 = pageTableMip0;
            compactShader.MaxRequests = (uint)desiredPages;
            compactShader.FrameStamp = frameStamp;
            compactShader.ScanOffset = 0u;
            compactShader.CompactMode = 1u;
            compactShader.Dispatch((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            GpuTestFence.WaitForGpuOrSkip("MaterialAtlas multi-frame compact pass dispatch");

            uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
            Assert.Equal((uint)desiredPages, requestCount);

            LumonScenePageRequestGpu[] requests = ReadSsbo<LumonScenePageRequestGpu>(pageRequests, itemCount: desiredPages);
            Array.Sort(requests, static (a, b) => a.VirtualPageIndex.CompareTo(b.VirtualPageIndex));

            cpuProc.Process(
                requests: requests,
                maxRequestsToProcess: desiredPages,
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

            captureComputeProgram.BindCaptureWorkSsbo(captureSsbo);
            captureComputeProgram.BindPatchMetaSsbo(patchMetaSsbo);
            captureComputeProgram.BindChunkSlotInfoSsbo(slotInfoSsbo);

            captureComputeProgram.BindDepthAtlasImage(depthAtlas);
            captureComputeProgram.BindMaterialAtlasImage(materialAtlas);

        using var sharedSurface = new SharedSurfaceInputFixture(occRes, occ, pal);
        captureComputeProgram.BindSharedGeometry(sharedSurface.Scene);

            captureComputeProgram.SetAtlasLayout((uint)tileSize, (uint)tilesPerAxis, (uint)tilesPerAtlas, 0u);

            int gx = (tileSize + 7) / 8;
            int gy = (tileSize + 7) / 8;
            captureComputeProgram.Dispatch(gx, gy, captureCount);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            GpuTestFence.WaitForGpuOrSkip("MaterialAtlas multi-frame capture pass dispatch");
        }

        // Assert all page mappings were allocated and the nominated texels in those atlas tiles were captured.
        Assert.Equal(desiredPages, virtualToPhysical.Count);

        var positions=new List<(int X,int Y,int Layer)>();
        for(uint physicalPageId=1;physicalPageId<=(uint)desiredPages;physicalPageId++)
        {
            DecodePhysicalId(physicalPageId,tilesPerAtlas,tilesPerAxis,out int atlasIndex,out int tileX,out int tileY);
            positions.Add((tileX*tileSize,tileY*tileSize,atlasIndex));
        }
        var proof=ImageAssertions.Predicates.PredicateAt(materialAtlas,positions, SurfacePredicates.LumonSceneMaterialAtlasMultiFramePopulationTests_MaterialAtlas_MultiFrame_SingleChunk_EventuallyPopulatesAllAllocatedTiles);
        proof.AssertEqual();Assert.Equal((uint)desiredPages,proof.MatchingTexels);
    }

    /// <summary>Verifies layered stamps return page feedback for every visible chunk slot.</summary>

    [Fact]
    public void PatchIdFeedback_When100ChunksVisible_ChunkSlotLayeredStamp_ReturnsPagesForAllSlots()
    {
        // Arrange layered usage stamps and repeated patch IDs across one hundred chunk slots.
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();

        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var markShaderOwner, out string markShaderLog), markShaderLog);
        using var markShader = markShaderOwner!;
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactShaderOwner, out string compactShaderLog), compactShaderLog);
        using var compactShader = compactShaderOwner!;

        const int chunksVisible = 100;
        const int pagesPerChunk = 16;
        const int totalPixels = chunksVisible * pagesPerChunk;

        const int gW = 160;
        const int gH = 10;
        Assert.True(gW * gH >= totalPixels);
        using var patchIdGBuffer = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, gW), Height = System.Math.Max(1, gH), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PatchIdGBuffer" }).RequireResource();


        int chunkSlotCount = chunksVisible;
        using var usageStamp = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageUsageStamp" }).RequireResource();
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);
        using var pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageTableMip0" }).RequireResource();

        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);
        using var genTex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = System.Math.Max(1, chunkSlotCount), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_ChunkSlotGeneration" }).RequireResource();

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
        using var markCounters = CreateAtomicCounterBuffer(counterCount: 3);

        // Act: mark visible page IDs, then compact the layered stamps into requests.
        markShader.BindDebugCounters(markCounters);
        markShader.PatchIdG = patchIdGBuffer;
        markShader.ChunkSlotGenerationTex = genTex;
        markShader.FrameStamp = 1u;
        markShader.BindPageUsageStampImage(usageStamp, access: ImageAccess.ReadWrite);
        markShader.Dispatch((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("MaterialAtlas mark pass dispatch");

        compactShader.BindRequestCounter(pageRequestCounter);
        compactShader.BindRequestsSsbo(pageRequests);
        compactShader.PageUsageStamp = usageStamp;
        compactShader.PageTableMip0 = pageTableMip0;
        compactShader.MaxRequests = (uint)totalPixels;
        compactShader.FrameStamp = 1u;
        compactShader.ScanOffset = 0u;
        compactShader.CompactMode = 1u;
        compactShader.Dispatch((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("MaterialAtlas compact pass dispatch");

        // Assert compaction returns the expected per-slot requests; this does not publish a page table.
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

    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("predicate-surface", 0, 0);
        programs.Prepare("predicate-surface-points1");
    }

    #endregion

    #region Private
    #region Atomic counter helpers
    /// <summary>Creates a zero-initialized atomic counter buffer with the requested number of counters.</summary>
    private static GpuAtomicCounterBuffer CreateAtomicCounterBuffer(int counterCount)
    {
        var buf = RHICommandContext.Current.CreateAtomicCounterBuffer(new RHIBufferDescription { Role = BufferRole.AtomicCounter, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Test_AtomicCounter" }).RequireResource();
        buf.InitializeCounters(counterCount, initialValue: 0u);
        return buf;
    }

    /// <summary>Resets one atomic counter before the next dispatch.</summary>
    private static void ResetAtomicCounter(GpuAtomicCounterBuffer buffer, int counterIndex)
    {
        Span<uint> zero = stackalloc uint[1] { 0u };
        buffer.UploadSubData((ReadOnlySpan<uint>)zero, dstOffsetBytes: counterIndex * sizeof(uint));
    }

    /// <summary>Reads one atomic counter after dispatch completion.</summary>
    private static uint ReadAtomicCounter(GpuAtomicCounterBuffer buffer, int counterIndex)
    {
        using var mapped = buffer.MapRange<uint>(dstOffsetBytes: counterIndex * sizeof(uint), elementCount: 1, access: BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span[0];
    }

    #endregion

    #region Shader-storage buffer helpers
    /// <summary>Allocates and uploads a typed span into a shader-storage buffer.</summary>
    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = name }).RequireResource();
        ssbo.Allocate(checked(data.Length * Unsafe.SizeOf<T>()));
        ssbo.UploadSubData(data, dstOffsetBytes: 0);
        return ssbo;
    }

    /// <summary>Allocates a typed shader-storage buffer with capacity for the requested item count.</summary>
    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, int capacityItems) where T : unmanaged
    {
        var ssbo = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = name }).RequireResource();
        ssbo.Allocate(checked(capacityItems * Unsafe.SizeOf<T>()));
        return ssbo;
    }

    /// <summary>Maps and copies the requested number of typed shader-storage elements.</summary>
    private static T[] ReadSsbo<T>(GpuShaderStorageBuffer ssbo, int itemCount) where T : unmanaged
    {
        T[] dst = new T[itemCount];
        using var mapped = ssbo.MapRange<T>(dstOffsetBytes: 0, elementCount: itemCount, access: BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        mapped.Span.CopyTo(dst);
        return dst;
    }

    #endregion

    #region Atlas initialization and page mapping helpers
    /// <summary>Fills every layer of a 2D-array texture with one scalar value.</summary>
    private static void FillR16f2DArray(int textureId, int width, int height, int depth, float value)
    {
        float[] data = new float[checked(width * height * depth)];
        Array.Fill(data, value);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Red, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    /// <summary>Fills every layer of a 2D-array texture with one RGBA8 value.</summary>
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

    /// <summary>Converts a physical page identifier into its atlas and tile coordinates.</summary>
    private static void DecodePhysicalId(uint physicalPageId, int tilesPerAtlas, int tilesPerAxis, out int atlasIndex, out int tileX, out int tileY)
    {
        int pageIndex = (int)physicalPageId - 1;
        atlasIndex = pageIndex / tilesPerAtlas;
        int local = pageIndex - (atlasIndex * tilesPerAtlas);
        tileY = local / tilesPerAxis;
        tileX = local - (tileY * tilesPerAxis);
    }

    #endregion

    #region Page-table writer substitute
    /// <summary>Discards page-table writes so the test can inspect page mappings and nominated atlas texels independently.</summary>
    private sealed class NoopPageTableWriter : ILumonScenePageTableWriter
    {
        /// <summary>Ignores publication; this test checks allocator mappings and selected material-atlas texels directly.</summary>
        public void WriteMip0(int chunkSlot, int virtualPageIndex, uint packedEntry) { }
    }

    #endregion
    #endregion

}
