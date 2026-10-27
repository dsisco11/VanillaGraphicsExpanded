using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Exposes explicit synchronization independently of graphics and compute selection.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Publishes earlier writes for the supplied subsequent access intent.</summary>
    public void Barrier(in RHIBarrierDescription description) => backend.Barrier(description);
    /// <summary>Inserts a fence; returning the owner does not imply GPU completion.</summary>
    public GpuFence InsertFence() => backend.InsertFence();
    /// <summary>Polls without flushing or waiting.</summary>
    public CompletionStatus Poll(GpuSyncRef fence) => backend.WaitCpu(fence, TimeSpan.Zero, false);
    /// <summary>Explicitly waits at most the given duration, optionally flushing submitted work.</summary>
    public CompletionStatus WaitCpu(GpuSyncRef fence, TimeSpan timeout, bool flush = true) => backend.WaitCpu(fence, timeout, flush);
    /// <summary>Orders GPU work after an existing fence without a CPU wait.</summary>
    public void WaitGpu(GpuSyncRef fence) => backend.WaitGpu(fence);
    /// <summary>Explicitly submits queued commands without adding a fence or waiting.</summary>
    public void Flush() => backend.Flush();
    /// <summary>Explicitly waits for all earlier GPU work; ordinary submission never invokes this implicitly.</summary>
    public void WaitIdle() => backend.WaitIdle();
    #endregion
}
