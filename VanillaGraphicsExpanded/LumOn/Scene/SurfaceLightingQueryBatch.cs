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
    private readonly GpuShaderStorageBuffer buffer = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamRead);
    private IntPtr fence;
    private int count;
    public bool Pending => fence != IntPtr.Zero;

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
        count=queries.Length;
        buffer.EnsureCapacity(count*64, growExponentially:false);
        buffer.UploadSubData(queries,0,count*64);
        using var program=pipeline.UseScope();
        geometry.Bind(scene); lighting.Bind(snapshot); buffer.BindBase(0);
        // Bind the exact active range: retained buffer capacity must not become extra queries.
        buffer.BindRange(0,0,count*64);
        GL.DispatchCompute((count+63)/64,1,1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        fence=GL.FenceSync(SyncCondition.SyncGpuCommandsComplete,WaitSyncFlags.None);
        GL.Flush();
    }

    /// <summary>Polls without blocking, mapping only after the completion fence signals.</summary>
    public bool TryRead(out SurfaceLightingQuery[] queries)
    {
        queries=Array.Empty<SurfaceLightingQuery>();
        if (!Pending) return false;
        var status=GL.ClientWaitSync(fence,ClientWaitSyncFlags.None,0);
        if (status==WaitSyncStatus.TimeoutExpired) return false;
        if (status==WaitSyncStatus.WaitFailed) throw new InvalidOperationException("Surface query fence failed.");
        using var read=buffer.MapRange<SurfaceLightingQuery>(0,count,MapBufferAccessMask.MapReadBit);
        if (!read.IsMapped) throw new InvalidOperationException("Surface query readback failed.");
        queries=read.Span.ToArray();
        GL.DeleteSync(fence); fence=IntPtr.Zero; count=0;
        return true;
    }
    #endregion

    #region Lifetime
    /// <summary>Retires submitted work and consumer-owned objects on their render context.</summary>
    public void Dispose()
    {
        if (Pending) GL.DeleteSync(fence);
        fence=IntPtr.Zero;
        buffer.Dispose(); geometry.Dispose(); lighting.Dispose(); pipeline.Dispose();
    }
    #endregion
}

