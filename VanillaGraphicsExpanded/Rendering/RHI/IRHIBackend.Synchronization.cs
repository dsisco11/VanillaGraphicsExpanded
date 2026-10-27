using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines ordered synchronization and explicit CPU completion operations.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Makes earlier writes visible to explicitly selected subsequent accesses.</summary>
    void Barrier(in RHIBarrierDescription description);
    /// <summary>Inserts an owned fence without waiting for completion.</summary>
    GpuFence InsertFence();
    /// <summary>Polls or explicitly waits for a fence on its originating context.</summary>
    CompletionStatus WaitCpu(GpuSyncRef fence, TimeSpan timeout, bool flush);
    /// <summary>Orders subsequent GPU work after a fence without blocking the CPU.</summary>
    void WaitGpu(GpuSyncRef fence);
    /// <summary>Explicitly submits queued native commands without waiting.</summary>
    void Flush();
    /// <summary>Explicitly completes prior GPU work for callers that already require whole-context retirement.</summary>
    void WaitIdle();
}
