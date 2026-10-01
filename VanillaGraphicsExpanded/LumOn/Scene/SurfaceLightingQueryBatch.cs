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
    private readonly SurfaceLightingQueryShader pipeline;
    private readonly SurfaceLightingParamsUbo lighting = new();

    private readonly GpuQueue<SurfaceLightingQuery> queue = new(MaximumQueries, debugName: "SurfaceLighting.Queries", usage: BufferUsageHint.StreamRead);
    public bool Pending => queue.Pending;

    #region Submission and completion
    /// <summary>Loads the production SPIR-V query program once per consumer lifetime.</summary>
    public SurfaceLightingQueryBatch(ICoreAPI api)
    {
        if (!GpuComputePipeline.TryCreateFromAssets(api, SurfaceLightingQueryShader.Contract.Identity,
            out var created, out _, out string log, preferSpirv:true)) throw new InvalidOperationException(log);
        pipeline=new(created!);
    }

    /// <summary>Copies a bounded descriptor batch and fences its GPU evaluation without waiting.</summary>
    public void Submit(TraceGeometryGpuScene scene, in SurfaceLightingSnapshot snapshot, ReadOnlySpan<SurfaceLightingQuery> queries)
    {
        if (Pending || queries.Length <= 0 || queries.Length > MaximumQueries) throw new InvalidOperationException("Invalid cache query admission.");
        int count=queries.Length;
        queue.WriteRecords(queries);

        pipeline.BindSharedGeometry(scene);
        lighting.Set(snapshot);
        pipeline.SurfaceLightingParameters = lighting;
        pipeline.CapturedMaterial = snapshot.Material; pipeline.PreviousOutgoing = snapshot.OutgoingRadiance;
        pipeline.SurfacePages = snapshot.PageTable; pipeline.SurfacePatches = snapshot.Patches;
        pipeline.SurfaceSlots = snapshot.Slots; pipeline.SurfaceReady = snapshot.Readiness;
        // Bind the exact active range: retained buffer capacity must not become extra queries.
        pipeline.SurfaceQueries = new(queue.Buffer, 0, count << 6);
        pipeline.Dispatch((count+63)/64,1,1);
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
        queue.Dispose(); lighting.Dispose(); pipeline.Dispose();
    }
    #endregion
}

