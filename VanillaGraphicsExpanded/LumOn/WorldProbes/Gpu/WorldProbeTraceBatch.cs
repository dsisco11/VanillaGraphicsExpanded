using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Collections;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Owns compact probe uploads, GPU-generated direction tiles and asynchronous indirect tracing.</summary>
internal sealed class WorldProbeTraceBatch : IDisposable
{
    public const int MaximumRays = 8192;
    private readonly GpuComputePipeline pipeline;
    private readonly GpuComputePipeline? setup;
    private readonly TraceGeometryComputeBindings geometry = new();
    private readonly SurfaceLightingBindings lighting = new();
    private GpuShaderStorageBuffer output = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamRead);
    private GpuComputePipeline? completion;
    private readonly GpuShaderStorageBuffer metadata = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamRead);
    private readonly GpuShaderStorageBuffer descriptors = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamRead);
    private readonly GpuShaderStorageBuffer descriptorCount = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamRead);
    private readonly GpuShaderStorageBuffer probes = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamDraw);
    private readonly GpuShaderStorageBuffer directions = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamDraw);
    private readonly GpuShaderStorageBuffer tiles = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw);
    private readonly GpuIndirectBuffer dispatch = GpuIndirectBuffer.Create();
    private GpuFence? fence;
    private int count;
    public bool Pending => fence is not null;

    #region Submission and completion
    /// <summary>Loads the work-list generator and tracing program on the render context.</summary>
    public WorldProbeTraceBatch(ICoreAPI api)
    {
        try
        {
            using var links = new ShaderLinkBatch(api.Assets, PBR.ShaderImportsSystem.DefaultDomain,
                [new ShaderSettings(WorldProbeTraceTilesShader.Contract), new ShaderSettings(WorldProbeTraceShader.Contract),
                 new ShaderSettings(WorldProbeCompletionShader.Contract)]);
            setup = GpuComputePipeline.DeclareFromAssets(api, new ShaderSettings(WorldProbeTraceTilesShader.Contract));
            pipeline = GpuComputePipeline.DeclareFromAssets(api, new ShaderSettings(WorldProbeTraceShader.Contract));
            completion = GpuComputePipeline.DeclareFromAssets(api, new ShaderSettings(WorldProbeCompletionShader.Contract));
            // This tracing owner requires all three programs; explicitly preload them through the active batch.
            if (!setup.EnsureReady()) throw new InvalidOperationException(setup.PreparationLog);
            if (!pipeline.EnsureReady()) throw new InvalidOperationException(pipeline.PreparationLog);
            if (!completion.EnsureReady()) throw new InvalidOperationException(completion.PreparationLog);
        }
        catch
        {
            pipeline?.Dispose(); completion?.Dispose(); metadata.Dispose(); descriptors.Dispose(); descriptorCount.Dispose();
            setup?.Dispose(); output.Dispose(); probes.Dispose(); directions.Dispose(); tiles.Dispose(); dispatch.Dispose();
            geometry.Dispose(); lighting.Dispose();
            throw;
        }
    }

    /// <summary>Uploads one record per probe and sparse texel indices; GPU work generation determines the trace dispatch.</summary>
    public void Submit(TraceGeometryGpuScene? scene, SurfaceLightingSnapshot? snapshot,
        ReadOnlySpan<WorldProbeTraceProbeGpu> probeData, ReadOnlySpan<uint> selectedDirections)
    {
        Validate(probeData, selectedDirections);
        count = selectedDirections.Length;
        int probeBytes = probeData.Length << 6;
        int directionBytes = count << 2;
        int outputBytes = count * 80;
        probes.EnsureCapacity(probeBytes, growExponentially: false);
        probes.UploadSubData(probeData, 0, probeBytes); probes.BindRange(4, 0, probeBytes);
        directions.EnsureCapacity(directionBytes, growExponentially: false);
        directions.UploadSubData(selectedDirections, 0, directionBytes); directions.BindRange(6, 0, directionBytes);
        // A probe can own as few as one direction; number of tiles never exceeds number of directions.
        tiles.EnsureCapacity(count << 3, growExponentially: false); tiles.BindRange(5, 0, count << 3);
        output.EnsureCapacity(outputBytes, growExponentially: false); output.BindRange(0, 0, outputBytes);
        dispatch.UploadDispatchCommand(new(0, 1, 1)); dispatch.BindStorage(7);
        using (setup!.UseScope()) GL.DispatchCompute((probeData.Length + 63) >> 6, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);
        using (pipeline.UseScope())
        using (dispatch.BindDispatchScope())
        {
            geometry.Bind(scene); lighting.Bind(snapshot);
            GL.DispatchComputeIndirect(IntPtr.Zero);
        }
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
        metadata.EnsureCapacity(count << 4, growExponentially: false); metadata.BindRange(1, 0, count << 4);
        descriptors.EnsureCapacity(outputBytes, growExponentially: false); descriptors.BindRange(2, 0, outputBytes);
        descriptorCount.EnsureCapacity(4, growExponentially: false);
        descriptorCount.UploadSubData<uint>(new uint[] { 0 }, 0, 4); descriptorCount.BindRange(3, 0, 4);
        using (completion!.UseScope()) GL.DispatchCompute((count + 63) >> 6, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
        fence = GpuFence.Insert();
        GL.Flush();
    }

    /// <summary>Validates disjoint complete output ownership and every selector before GPU-generated work can reference it.</summary>
    private void Validate(ReadOnlySpan<WorldProbeTraceProbeGpu> probeData, ReadOnlySpan<uint> selected)
    {
        if (Pending || probeData.IsEmpty || selected.IsEmpty || selected.Length > MaximumRays || probeData.Length > selected.Length)
            throw new InvalidOperationException("Invalid probe trace admission.");
        uint next = 0;
        foreach (var probe in probeData)
        {
            if (probe.OctahedralSize is < 1 or > 64 || probe.FirstDirection != next || probe.DirectionCount == 0 ||
                probe.DirectionCount > (uint)selected.Length - next || probe.MaxSteps > 512)
                throw new InvalidOperationException("Invalid probe direction ownership.");
            uint end = next + probe.DirectionCount;
            for (uint index = next; index < end; index++)
            {
                uint selector = selected[(int)index];
                bool cardinal = (selector & 0x80000000u) != 0;
                if (cardinal ? (selector & 0x7fffffffu) >= 6 || probe.NearbyDistance.X <= 0
                    : selector >= probe.OctahedralSize * probe.OctahedralSize)
                    throw new InvalidOperationException("Invalid probe direction selector.");
            }
            next = end;
        }
        if (next != selected.Length) throw new InvalidOperationException("Unowned probe directions.");
    }

    /// <summary>Polls without blocking, then freezes only initialized answers after indirect tracing finishes.</summary>
    public bool TryRead(out ImmutableArray<WorldProbeTraceAnswerGpu> answers)
    {
        answers = ImmutableArray<WorldProbeTraceAnswerGpu>.Empty;
        if (fence is null) return false;
        var status = fence.Poll();
        if (status == WaitSyncStatus.TimeoutExpired) return false;
        if (status == WaitSyncStatus.WaitFailed) throw new InvalidOperationException("World-probe trace fence failed.");
        using var read = output.MapRange<WorldProbeTraceAnswerGpu>(0, count, MapBufferAccessMask.MapReadBit);
        if (!read.IsMapped) throw new InvalidOperationException("World-probe trace readback failed.");
        var builder = ImmutableArray.CreateBuilder<WorldProbeTraceAnswerGpu>(count);
        foreach (var answer in read.Span) builder.Add(answer);
        answers = builder.MoveToImmutable();
        fence.Dispose(); fence = null; count = 0;
        return true;
    }

    /// <summary>Reads bounded completion metadata and sparse missing-light descriptors, transferring resident RGB ownership.</summary>
    public bool TryReadResident(out ImmutableArray<WorldProbeTraceAnswerGpu> answers, out WorldProbeResidentAnswers? resident)
    {
        answers = default; resident = null;
        if (fence is null) return false;
        var status = fence.Poll();
        if (status == WaitSyncStatus.TimeoutExpired) return false;
        if (status == WaitSyncStatus.WaitFailed) throw new InvalidOperationException("World-probe completion fence failed.");
        using var totals = descriptorCount.MapRange<uint>(0, 1, MapBufferAccessMask.MapReadBit);
        if (!totals.IsMapped || totals.Span[0] > count) throw new InvalidOperationException("Invalid descriptor completion count.");
        int sparseCount = (int)totals.Span[0];
        using var sparse = PooledArray<WorldProbeTraceAnswerGpu>.Rent(sparseCount);
        if (sparseCount > 0)
        {
            using var read = descriptors.MapRange<WorldProbeTraceAnswerGpu>(0, sparseCount, MapBufferAccessMask.MapReadBit);
            if (!read.IsMapped) throw new InvalidOperationException("Descriptor readback failed.");
            read.Span.CopyTo(sparse.Span);
        }
        using var data = metadata.MapRange<WorldProbeCompletionGpu>(0, count, MapBufferAccessMask.MapReadBit);
        if (!data.IsMapped) throw new InvalidOperationException("Completion readback failed.");
        var builder = ImmutableArray.CreateBuilder<WorldProbeTraceAnswerGpu>(count);
        foreach (var item in data.Span)
        {
            if (item.Descriptor >= sparseCount || item.Descriptor < -2) throw new InvalidOperationException("Invalid descriptor index.");
            var answer = item.Descriptor >= 0 ? sparse.Span[item.Descriptor] : new WorldProbeTraceAnswerGpu
            { Outcome = item.Outcome, Reason = item.Reason };
            answer.Hit.Fraction.W = item.Distance;
            if (item.Descriptor == -2) answer.Hit.Result.W = 1;
            builder.Add(answer);
        }
        answers = builder.MoveToImmutable();
        LastReadbackBytes = 4 + (count << 4) + sparseCount * 80;
        var replacement = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamRead);
        resident = new WorldProbeResidentAnswers(output, count); output = replacement;
        fence.Dispose(); fence = null; count = 0;
        return true;
    }

    /// <summary>Bytes mapped by the last compact completion, excluding resident directional payloads.</summary>
    internal int LastReadbackBytes { get; private set; }
    #endregion

    #region Lifetime
    /// <summary>Retires owned work, output and indirect argument storage while leaving borrowed scene resources intact.</summary>
    public void Dispose()
    {
        fence?.Dispose(); fence = null;
        output.Dispose(); probes.Dispose(); directions.Dispose(); tiles.Dispose(); dispatch.Dispose();
        metadata.Dispose(); descriptors.Dispose(); descriptorCount.Dispose(); completion?.Dispose();
        geometry.Dispose(); lighting.Dispose(); pipeline.Dispose(); setup?.Dispose();
    }
    #endregion
}
