using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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
public sealed class LumonScenePatchIdFeedbackLimitsTests : RenderTestBase
{
    public LumonScenePatchIdFeedbackLimitsTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void PatchIdFeedback_MarkCompact_CanReturnManyUniquePages_UpToMaxRequests()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var markComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_mark_pages", debugName: "Tests.PatchIdFeedbackLimits.Mark");
        using var compactComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_compact_pages", debugName: "Tests.PatchIdFeedbackLimits.Compact");
        int markProgram = markComputeProgram.ProgramId;
        int compactProgram = compactComputeProgram.ProgramId;

        const int desiredPages = 4096;
        const int gW = 128;
        const int gH = 32;
        Assert.Equal(desiredPages, gW * gH);

        using var patchIdGBuffer = Texture2D.Create(gW, gH, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

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
        GL.UseProgram(markProgram);
        markCounters.BindBase(bindingIndex: 0);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
        BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
        using var markParamsUbo = new ObjectParamsUbo("Tests.PatchIdFeedbackLimits.Mark.ParamsUBO");
        LumonSceneFeedbackParamsUbo.BindMark(markParamsUbo, frameStamp: 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits mark pass dispatch (case 1)");

        // Pass B: compact.
        GL.UseProgram(compactProgram);
        pageRequestCounter.BindBase(bindingIndex: 0);
        pageRequests.BindBase(bindingIndex: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageUsageStamp", usageStamp.TextureId, unit: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageTableMip0", pageTableMip0.TextureId, unit: 1);
        using var compactParamsUbo = new ObjectParamsUbo("Tests.PatchIdFeedbackLimits.Compact.ParamsUBO");
        LumonSceneFeedbackParamsUbo.BindCompact(
            compactParamsUbo,
            maxRequests: (uint)desiredPages,
            frameStamp: 1u,
            scanOffset: 0u,
            compactMode: 1u);
        GL.DispatchCompute((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits compact pass dispatch (case 1)");

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

        // Programs are disposed via ComputeProgram.
    }

    [Fact]
    public void PatchIdFeedback_Mark_IgnoresChunkSlotsOutsideStampLayerRange()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var markComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_mark_pages", debugName: "Tests.PatchIdFeedbackLimits2.Mark");
        using var compactComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_compact_pages", debugName: "Tests.PatchIdFeedbackLimits2.Compact");
        int markProgram = markComputeProgram.ProgramId;
        int compactProgram = compactComputeProgram.ProgramId;

        const int gW = 16;
        const int gH = 16;
        const int desiredPages = 32;

        using var patchIdGBuffer = Texture2D.Create(gW, gH, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

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

        GL.UseProgram(markProgram);
        markCounters.BindBase(bindingIndex: 0);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
        BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
        using var markParamsUbo = new ObjectParamsUbo("Tests.PatchIdFeedbackLimits2.Mark.ParamsUBO");
        LumonSceneFeedbackParamsUbo.BindMark(markParamsUbo, frameStamp: 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits mark pass dispatch (case 2)");

        GL.UseProgram(compactProgram);
        pageRequestCounter.BindBase(bindingIndex: 0);
        pageRequests.BindBase(bindingIndex: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageUsageStamp", usageStamp.TextureId, unit: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageTableMip0", pageTableMip0.TextureId, unit: 1);
        using var compactParamsUbo = new ObjectParamsUbo("Tests.PatchIdFeedbackLimits2.Compact.ParamsUBO");
        LumonSceneFeedbackParamsUbo.BindCompact(
            compactParamsUbo,
            maxRequests: (uint)desiredPages,
            frameStamp: 1u,
            scanOffset: 0u,
            compactMode: 1u);
        GL.DispatchCompute((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits compact pass dispatch (case 2)");

        uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
        Assert.Equal(0u, requestCount);

        // Programs are disposed via ComputeProgram.
    }

    [Fact]
    public void PatchIdFeedback_MarkCompact_ModuloCollisions_CanCollapseToFewPages()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var markComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_mark_pages", debugName: "Tests.PatchIdFeedbackLimits3.Mark");
        using var compactComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_compact_pages", debugName: "Tests.PatchIdFeedbackLimits3.Compact");
        int markProgram = markComputeProgram.ProgramId;
        int compactProgram = compactComputeProgram.ProgramId;

        const int desiredPages = 1024;
        const int gW = 64;
        const int gH = 16;
        Assert.Equal(desiredPages, gW * gH);

        using var patchIdGBuffer = Texture2D.Create(gW, gH, PixelInternalFormat.Rgba32ui, debugName: "Test_PatchIdGBuffer");

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

        GL.UseProgram(markProgram);
        markCounters.BindBase(bindingIndex: 0);
        BindSampler2DUint(markProgram, "vge_patchIdGBuffer", patchIdGBuffer.TextureId, unit: 0);
        BindSampler2DUint(markProgram, "vge_chunkSlotGenerationTex", genTex.TextureId, unit: 1);
        using var markParamsUbo = new ObjectParamsUbo("Tests.PatchIdFeedbackLimits3.Mark.ParamsUBO");
        LumonSceneFeedbackParamsUbo.BindMark(markParamsUbo, frameStamp: 1u);
        GL.BindImageTexture(0, usageStamp.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.ReadWrite, format: SizedInternalFormat.R32ui);
        GL.DispatchCompute((gW + 7) / 8, (gH + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits mark pass dispatch (case 3)");

        GL.UseProgram(compactProgram);
        pageRequestCounter.BindBase(bindingIndex: 0);
        pageRequests.BindBase(bindingIndex: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageUsageStamp", usageStamp.TextureId, unit: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageTableMip0", pageTableMip0.TextureId, unit: 1);
        using var compactParamsUbo = new ObjectParamsUbo("Tests.PatchIdFeedbackLimits3.Compact.ParamsUBO");
        LumonSceneFeedbackParamsUbo.BindCompact(
            compactParamsUbo,
            maxRequests: (uint)desiredPages,
            frameStamp: 1u,
            scanOffset: 0u,
            compactMode: 1u);
        GL.DispatchCompute((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits compact pass dispatch (case 3)");

        uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
        Assert.Equal(1u, requestCount);

        LumonScenePageRequestGpu[] requests = ReadSsbo<LumonScenePageRequestGpu>(pageRequests, itemCount: 1);
        Assert.Equal(1u, requests[0].VirtualPageIndex);

        // Programs are disposed via ComputeProgram.
    }

    [Fact]
    public void PatchIdFeedback_Compaction_IsGatedByFrameStamp()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var compactComputeProgram = ComputeProgram.Create(helper, "lumonscene_feedback_compact_pages", debugName: "Tests.PatchIdFeedbackLimits4.Compact");
        int compactProgram = compactComputeProgram.ProgramId;

        const int chunkSlotCount = 1;
        using var usageStamp = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageUsageStamp");

        // Pretend mark pass wrote frameStamp=1 to one page.
        uint[] stamp = new uint[128 * 128 * chunkSlotCount];
        stamp[1] = 1u;
        usageStamp.UploadDataImmediate(stamp, 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageTableMip0 = Texture3D.Create(
            128,
            128,
            chunkSlotCount,
            PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: "Test_PageTableMip0");
        pageTableMip0.UploadDataImmediate(new uint[128 * 128 * chunkSlotCount], 0, 0, 0, 128, 128, chunkSlotCount);

        using var pageRequests = CreateSsbo<LumonScenePageRequestGpu>("Test_PageRequests", capacityItems: 8);
        using var pageRequestCounter = CreateAtomicCounterBuffer(initialValue: 0u, counterCount: 1);

        GL.UseProgram(compactProgram);
        pageRequestCounter.BindBase(bindingIndex: 0);
        pageRequests.BindBase(bindingIndex: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageUsageStamp", usageStamp.TextureId, unit: 0);
        BindSampler2DArrayUint(compactProgram, "vge_pageTableMip0", pageTableMip0.TextureId, unit: 1);
        using var compactParamsUbo = new ObjectParamsUbo("Tests.PatchIdFeedbackLimits4.Compact.ParamsUBO");
        LumonSceneFeedbackParamsUbo.BindCompact(
            compactParamsUbo,
            maxRequests: 8u,
            frameStamp: 2u, // mismatch => no requests
            scanOffset: 0u,
            compactMode: 1u);
        GL.DispatchCompute((LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk * chunkSlotCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("PatchIdFeedbackLimits compact pass dispatch (case 4)");

        uint requestCount = ReadAtomicCounter(pageRequestCounter, counterIndex: 0);
        Assert.Equal(0u, requestCount);

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

    private static void SetUniform1ui(int program, string name, uint value)
    {
        int loc = global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(program, name);
        if (loc < 0 && ComputeProgram.TryGetExplicitUniformLocation(program, name, out int explicitLoc))
        {
            loc = explicitLoc;
        }

        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static GpuAtomicCounterBuffer CreateAtomicCounterBuffer(uint initialValue, int counterCount)
    {
        var buf = GpuAtomicCounterBuffer.Create(BufferUsageHint.DynamicDraw, debugName: "Test_AtomicCounter");
        buf.InitializeCounters(counterCount, initialValue);
        return buf;
    }

    private static uint ReadAtomicCounter(GpuAtomicCounterBuffer buffer, int counterIndex)
    {
        using var mapped = buffer.MapRange<uint>(dstOffsetBytes: counterIndex * sizeof(uint), elementCount: 1, access: MapBufferAccessMask.MapReadBit);
        Assert.True(mapped.IsMapped);
        return mapped.Span[0];
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, int capacityItems) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        ssbo.Allocate(checked(capacityItems * Unsafe.SizeOf<T>()));
        return ssbo;
    }

    private static T[] ReadSsbo<T>(GpuShaderStorageBuffer ssbo, int itemCount) where T : unmanaged
    {
        T[] dst = new T[itemCount];
        using var mapped = ssbo.MapRange<T>(dstOffsetBytes: 0, elementCount: itemCount, access: MapBufferAccessMask.MapReadBit);
        Assert.True(mapped.IsMapped);
        mapped.Span.CopyTo(dst);
        return dst;
    }
}
