using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;

using Xunit;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises lumon scene patch id feedback limits behavior using the test harness.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumonScenePatchIdFeedbackLimitsTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context to verify feedback patch-ID bounds and counter limits.</summary>
    public LumonScenePatchIdFeedbackLimitsTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Verifies compaction can return many unique pages up to the request limit.</summary>

    [Fact]
    public void PatchIdFeedback_MarkCompact_CanReturnManyUniquePages_UpToMaxRequests()
    {
        // Arrange unique patch IDs and request storage sized to the supported maximum.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var markComputeProgramOwner, out string markComputeProgramLog), markComputeProgramLog);
        using var markComputeProgram = markComputeProgramOwner!;
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactComputeProgramOwner, out string compactComputeProgramLog), compactComputeProgramLog);
        using var compactComputeProgram = compactComputeProgramOwner!;

        const int desiredPages = 4096;
        const int gW = 128;
        const int gH = 32;
        Assert.Equal(desiredPages, gW * gH);

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
            pidTexels[idx + 0] = 0u;          // chunkSlot
            pidTexels[idx + 1] = (uint)(i + 1); // patchId
            pidTexels[idx + 2] = 0u;
            pidTexels[idx + 3] = 0u;
        }
        patchIdGBuffer.UploadDataImmediate(pidTexels);

        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: desiredPages);
        using var pageRequestCounter = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 1);
        using var markCounters = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 3);

        // Pass A: mark pages.
        markComputeProgram.BindDebugCounters(markCounters);
        markComputeProgram.PatchIdG = patchIdGBuffer;
        markComputeProgram.ChunkSlotGenerationTex = genTex;

        markComputeProgram.FrameStamp = 1u;
        markComputeProgram.BindPageUsageStampImage(usageStamp);
        // Act: mark unique page usage and compact it into the bounded request buffer.
        markComputeProgram.Dispatch((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits mark pass dispatch (case 1)");

        // Pass B: compact.
        compactComputeProgram.BindRequestCounter(pageRequestCounter);
        compactComputeProgram.BindRequestsSsbo(pageRequests);
        compactComputeProgram.PageUsageStamp = usageStamp;
        compactComputeProgram.PageTableMip0 = pageTableMip0;

        compactComputeProgram.MaxRequests = (uint)desiredPages;
        compactComputeProgram.FrameStamp = 1u;
        compactComputeProgram.ScanOffset = 0u;
        compactComputeProgram.CompactMode = 1u;
        compactComputeProgram.Dispatch((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits compact pass dispatch (case 1)");

        // Assert the output contains the full unique set without exceeding the request limit.
        uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
        Assert.Equal((uint)desiredPages, requestCount);

        LumonScenePageRequestGpu[] requests = ReadSsbo<LumonScenePageRequestGpu>(pageRequests, itemCount: desiredPages);

        var seen = new HashSet<uint>(capacity: desiredPages);
        for (int i = 0; i < requests.Length; i++)
        {
            Assert.Equal(0u, requests[i].ChunkSlot);
            Assert.Equal(0u, requests[i].Mip);

            uint v = requests[i].VirtualPageIndex;
            Assert.InRange(v, 0u, (uint)(LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk - 1));
            seen.Add(v);
        }

        // With patchId = 1..4096 and virtualPageIndex = patchId % 16384, we expect 4096 unique vpages.
        Assert.Equal(desiredPages, seen.Count);

    }

    /// <summary>Verifies feedback marking ignores chunk slots beyond available stamp layers.</summary>

    [Fact]
    public void PatchIdFeedback_Mark_IgnoresChunkSlotsOutsideStampLayerRange()
    {
        // Arrange feedback pixels that reference chunk slots beyond the stamp texture depth.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var markComputeProgramOwner, out string markComputeProgramLog), markComputeProgramLog);
        using var markComputeProgram = markComputeProgramOwner!;
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactComputeProgramOwner, out string compactComputeProgramLog), compactComputeProgramLog);

        using var compactComputeProgram = compactComputeProgramOwner!;
        const int gW = 16;
        const int gH = 16;
        const int desiredPages = 32;


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
            pidTexels[idx + 0] = 1u;            // chunkSlot outside stamp range => ignored by shader
            pidTexels[idx + 1] = (uint)(i + 1); // patchId
        }
        patchIdGBuffer.UploadDataImmediate(pidTexels);


        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: desiredPages);
        using var pageRequestCounter = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 1);
        using var markCounters = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 3);
        markComputeProgram.BindDebugCounters(markCounters);
        markComputeProgram.PatchIdG = patchIdGBuffer;
        markComputeProgram.ChunkSlotGenerationTex = genTex;

        markComputeProgram.FrameStamp = 1u;
        markComputeProgram.BindPageUsageStampImage(usageStamp);
        // Act: process the out-of-range slots through mark and compact passes.
        markComputeProgram.Dispatch((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits mark pass dispatch (case 2)");

        compactComputeProgram.BindRequestCounter(pageRequestCounter);
        compactComputeProgram.BindRequestsSsbo(pageRequests);
        compactComputeProgram.PageUsageStamp = usageStamp;
        compactComputeProgram.PageTableMip0 = pageTableMip0;

        compactComputeProgram.MaxRequests = (uint)desiredPages;
        compactComputeProgram.FrameStamp = 1u;
        compactComputeProgram.ScanOffset = 0u;
        compactComputeProgram.CompactMode = 1u;
        compactComputeProgram.Dispatch((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits compact pass dispatch (case 2)");

        // Assert invalid slots contribute no compacted page requests.
        uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
        Assert.Equal(0u, requestCount);

    }

    /// <summary>Verifies modulo collisions may collapse compacted feedback to fewer unique pages.</summary>

    [Fact]
    public void PatchIdFeedback_MarkCompact_ModuloCollisions_CanCollapseToFewPages()
    {
        // Arrange distinct patch IDs whose page indices collide modulo the configured page count.
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var markComputeProgramOwner, out string markComputeProgramLog), markComputeProgramLog);
        using var markComputeProgram = markComputeProgramOwner!;
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactComputeProgramOwner, out string compactComputeProgramLog), compactComputeProgramLog);

        using var compactComputeProgram = compactComputeProgramOwner!;
        const int desiredPages = 1024;
        const int gW = 64;
        const int gH = 16;
        Assert.Equal(desiredPages, gW * gH);


        using var patchIdGBuffer = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, gW), Height = System.Math.Max(1, gH), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PatchIdGBuffer" }).RequireResource();
        const int chunkSlotCount = 1;
        using var usageStamp = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageUsageStamp" }).RequireResource();
        usageStamp.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageTableMip0" }).RequireResource();
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var genTex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32ui, Width = System.Math.Max(1, chunkSlotCount), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_ChunkSlotGeneration" }).RequireResource();
        genTex.UploadDataImmediate(new uint[chunkSlotCount], x: 0, y: 0, regionWidth: chunkSlotCount, regionHeight: 1);

        // Choose patchIds that all map to virtualPageIndex==1 via modulo 16384.
        uint[] pidTexels = new uint[gW * gH * 4];
        for (int i = 0; i < desiredPages; i++)
        {
            int idx = i * 4;
            pidTexels[idx + 0] = 0u;
            pidTexels[idx + 1] = 1u + (uint)(i * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk);
        }
        patchIdGBuffer.UploadDataImmediate(pidTexels);


        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: desiredPages);
        using var pageRequestCounter = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 1);
        using var markCounters = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 3);
        markComputeProgram.BindDebugCounters(markCounters);
        markComputeProgram.PatchIdG = patchIdGBuffer;
        markComputeProgram.ChunkSlotGenerationTex = genTex;

        markComputeProgram.FrameStamp = 1u;
        markComputeProgram.BindPageUsageStampImage(usageStamp);
        // Act: mark the colliding IDs and compact their shared page stamp.
        markComputeProgram.Dispatch((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits mark pass dispatch (case 3)");

        compactComputeProgram.BindRequestCounter(pageRequestCounter);
        compactComputeProgram.BindRequestsSsbo(pageRequests);
        compactComputeProgram.PageUsageStamp = usageStamp;
        compactComputeProgram.PageTableMip0 = pageTableMip0;

        compactComputeProgram.MaxRequests = (uint)desiredPages;
        compactComputeProgram.FrameStamp = 1u;
        compactComputeProgram.ScanOffset = 0u;
        compactComputeProgram.CompactMode = 1u;
        compactComputeProgram.Dispatch((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits compact pass dispatch (case 3)");

        // Assert collisions collapse to one request for the expected virtual page.
        uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
        Assert.Equal(1u, requestCount);

        LumonScenePageRequestGpu[] requests = ReadSsbo<LumonScenePageRequestGpu>(pageRequests, itemCount: 1);
        Assert.Equal(1u, requests[0].VirtualPageIndex);

    }

    /// <summary>Verifies feedback compaction only emits requests marked for the active frame.</summary>

    [Fact]
    public void PatchIdFeedback_Compaction_IsGatedByFrameStamp()
    {
        // Arrange a page stamped in an earlier frame and select a different frame for compaction.
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();

        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var compactComputeProgramOwner, out string compactComputeProgramLog), compactComputeProgramLog);
        using var compactComputeProgram = compactComputeProgramOwner!;

        const int chunkSlotCount = 1;
        using var usageStamp = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageUsageStamp" }).RequireResource();

        // Pretend mark pass wrote frameStamp=1 to one page.
        uint[] stamp = new uint[128 * 128 * chunkSlotCount];
        stamp[1] = 1u;
        usageStamp.UploadDataImmediate(stamp, 0, 0, 0, 128, 128, chunkSlotCount);
        using var pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 128), Height = System.Math.Max(1, 128), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_PageTableMip0" }).RequireResource();

        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: 8);
        using var pageRequestCounter = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 1);

        compactComputeProgram.BindRequestCounter(pageRequestCounter);
        compactComputeProgram.BindRequestsSsbo(pageRequests);
        compactComputeProgram.PageUsageStamp = usageStamp;
        compactComputeProgram.PageTableMip0 = pageTableMip0;

        compactComputeProgram.MaxRequests = 8u;
        compactComputeProgram.FrameStamp = 2u;
        compactComputeProgram.ScanOffset = 0u;
        compactComputeProgram.CompactMode = 1u;
        // Act: compact using the selected frame stamp.
        compactComputeProgram.Dispatch((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits compact pass dispatch (case 4)");

        // Assert the prior frame's stamp is excluded from the request list.
        uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
        Assert.Equal(0u, requestCount);

    }

    #endregion

    #region Private
    /// <summary>Creates an atomic counter buffer initialized for the test.</summary>
    private static GpuAtomicCounterBuffer CreateAtomicCounterBuffer(uint initialValue, int counterCount)
    {
        var buf = RHICommandContext.Current.CreateAtomicCounterBuffer(new RHIBufferDescription { Role = BufferRole.AtomicCounter, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Test_AtomicCounter" }).RequireResource();
        buf.InitializeCounters(counterCount, initialValue);
        return buf;
    }

    /// <summary>Reads one atomic counter after dispatch completion.</summary>
    private static uint ReadAtomicCounter(GpuAtomicCounterBuffer buffer, int counterIndex)
    {
        using var mapped = buffer.MapRange<uint>(dstOffsetBytes: counterIndex * sizeof(uint), elementCount: 1, access: BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span[0];
    }

    /// <summary>Allocates an empty typed shader-storage buffer for the requested item capacity.</summary>
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

}
