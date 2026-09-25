using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
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
    private readonly GpuShaderStorageBuffer output = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamRead);
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
            if (!GpuComputePipeline.TryCreateFromAssets(api, WorldProbeTraceTilesShader.Contract.Identity,
                out var setupProgram, out _, out string setupLog, preferSpirv: true)) throw new InvalidOperationException(setupLog);
            setup = setupProgram!;
            if (!GpuComputePipeline.TryCreateFromAssets(api, WorldProbeTraceShader.Contract.Identity,
                out var created, out _, out string log, preferSpirv: true)) throw new InvalidOperationException(log);
            pipeline = created!;
        }
        catch
        {
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
    #endregion

    #region Lifetime
    /// <summary>Retires owned work, output and indirect argument storage while leaving borrowed scene resources intact.</summary>
    public void Dispose()
    {
        fence?.Dispose(); fence = null;
        output.Dispose(); probes.Dispose(); directions.Dispose(); tiles.Dispose(); dispatch.Dispose();
        geometry.Dispose(); lighting.Dispose(); pipeline.Dispose(); setup?.Dispose();
    }
    #endregion
}
