using System;
using System.Diagnostics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Accumulates render-thread wall intervals, including driver waits rather than claiming CPU execution time.</summary>
internal sealed class SurfaceCpuTiming
{
    public long Count { get; private set; }
    public double Milliseconds { get; private set; }

    #region Measurement
    /// <summary>Starts a stack-owned interval that also closes on an early return or exception.</summary>
    public Interval Measure() => new(this);

    /// <summary>Clears totals when the owning world leaves.</summary>
    public void Clear() { Count = 0; Milliseconds = 0; }

    /// <summary>Nonallocating render-thread wall interval; owners dispose it exactly once.</summary>
    internal readonly struct Interval : IDisposable
    {
        private readonly SurfaceCpuTiming owner;
        private readonly long start;

        /// <summary>Captures the current monotonic timestamp.</summary>
        public Interval(SurfaceCpuTiming owner) { this.owner = owner; start = Stopwatch.GetTimestamp(); }

        /// <summary>Adds inclusive elapsed wall time to the owning aggregate.</summary>
        public void Dispose() { owner.Count++; owner.Milliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }
    #endregion
}
