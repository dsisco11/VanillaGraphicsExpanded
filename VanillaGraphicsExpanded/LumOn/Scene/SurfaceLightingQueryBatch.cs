using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>One bounded asynchronous render-thread cache query; tracing workers never access GL resources.</summary>
internal sealed class SurfaceLightingQueryBatch : IDisposable
{
    public const int MaximumQueries = 4096;
    private readonly GpuComputePipeline pipeline;
    private readonly SurfaceLightingBindings lighting = new();
    private readonly TraceGeometryComputeBindings geometry = new();
    private readonly GpuQueue<SurfaceLightingQuery> queue = new(MaximumQueries, debugName: "SurfaceLighting.Queries", usage: BufferUsageHint.StreamRead);
    public bool Pending => queue.Pending;

    #region Submission and completion
    /// <summary>Loads the production SPIR-V query program once per consumer lifetime.</summary>
    public SurfaceLightingQueryBatch(ICoreAPI api)
    {
        if (!GpuComputePipeline.TryCreateFromAssets(api, SurfaceLightingQueryShader.Contract.Identity,
            out var created, out _, out string log, preferSpirv:true)) throw new InvalidOperationException(log);
        pipeline=created!;
    }

    /// <summary>Copies a bounded descriptor batch and fences its GPU evaluation without waiting.</summary>
    public void Submit(TraceGeometryGpuScene scene, in SurfaceLightingSnapshot snapshot, ReadOnlySpan<SurfaceLightingQuery> queries)
    {
        if (Pending || queries.Length <= 0 || queries.Length > MaximumQueries) throw new InvalidOperationException("Invalid cache query admission.");
        int count=queries.Length;
        queue.WriteRecords(queries);
        using var program=pipeline.UseScope();
        geometry.Bind(scene); lighting.Bind(snapshot);
        // Bind the exact active range: retained buffer capacity must not become extra queries.
        queue.Buffer.BindRange(0,0,count << 6);
        GL.DispatchCompute((count+63)/64,1,1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        queue.Submit(count);
    }

    /// <summary>Polls without blocking, mapping only after the completion fence signals.</summary>
    public bool TryRead(out SurfaceLightingQuery[] queries)
    {
        queries=Array.Empty<SurfaceLightingQuery>();
        if (!queue.TryRead<SurfaceLightingQuery[]>(static records => records.ToArray(), out var completed)) return false;
        queries=completed;
        return true;
    }
    #endregion

    #region Lifetime
    /// <summary>Retires submitted work and consumer-owned objects on their render context.</summary>
    public void Dispose()
    {
        queue.Dispose(); geometry.Dispose(); lighting.Dispose(); pipeline.Dispose();
    }
    #endregion
}

