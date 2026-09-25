using System;
using System.Collections.Immutable;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Collects bounded dispatch counters and timestamp intervals without waiting for GPU completion.</summary>
internal sealed class SurfaceWorkDiagnostics : IDisposable
{
    private const int Capacity = 8;
    private readonly Slot?[] slots = new Slot[Capacity];
    private readonly Totals[] totals = new Totals[(int)SurfaceWorkStage.Count];
    private readonly uint[] zero = new uint[(int)SurfaceWorkCounter.Count];
    private int active = -1;
    private SurfaceWorkStage activeStage;
    private long started;
    private bool disposed;
    public bool Enabled { get; set; } = true;
    public int PendingCount { get; private set; }

    #region Collection
    /// <summary>Initializes CPU aggregates; GPU objects are allocated only when an enabled sample is admitted.</summary>
    public SurfaceWorkDiagnostics()
    {
        for (int i = 0; i < totals.Length; i++) totals[i] = new Totals();
    }

    /// <summary>Admits one sample or skips instrumentation when the bounded ring is occupied.</summary>
    public bool Begin(SurfaceWorkStage stage, int pages)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started != 0) throw new InvalidOperationException("Nested Surface Cache diagnostic dispatch.");
        Poll();
        activeStage = stage;
        started = Stopwatch.GetTimestamp();
        var total = totals[(int)stage];
        total.Submitted++; total.Pages += pages;
        if (Enabled)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i] ??= new Slot();
                if (slot.Fence != null) continue;
                active = i; slot.Stage = stage; slot.Started = started;
                slot.Buffer.UploadSubData<uint>(zero, 0, zero.Length << 2);
                slot.Buffer.BindBase(4);
                slot.Start.Issue();
                return true;
            }
        }
        total.Skipped++;
        return false;
    }

    /// <summary>Closes the GPU interval and fences its counters; never retrieves a result here.</summary>
    public void End()
    {
        if (started == 0) return;
        if (active >= 0)
        {
            var slot = slots[active]!;
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
            slot.End.Issue(); slot.Fence = GpuFence.Insert(); PendingCount++;
        }
        totals[(int)activeStage].SubmitMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        active = -1; started = 0;
    }

    /// <summary>Reads only signaled samples with available timestamps; saturation drops diagnostics, never work.</summary>
    public void Poll()
    {
        if (disposed) return;
        for (int index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            if (slot?.Fence == null) continue;
            var status = slot.Fence.Poll();
            if (status == WaitSyncStatus.TimeoutExpired) continue;
            var total = totals[(int)slot.Stage];
            if (status == WaitSyncStatus.WaitFailed)
            {
                // Completion is unknown: retire the storage itself rather than overwrite a possibly live buffer.
                total.ReadFailures++; slot.Dispose(); slots[index] = null; PendingCount--; continue;
            }
            if (!slot.Start.TryGetResultNanoseconds(out long start) || !slot.End.TryGetResultNanoseconds(out long end)) continue;
            using var mapped = slot.Buffer.MapRange<uint>(0, zero.Length, MapBufferAccessMask.MapReadBit);
            if (mapped.IsMapped)
            {
                for (int i = 0; i < zero.Length; i++) total.Counters[i] += mapped.Span[i];
                total.Collected++;
                total.GpuMilliseconds += Math.Max(0, end - start) / 1e6;
                total.CompletionMilliseconds += Stopwatch.GetElapsedTime(slot.Started).TotalMilliseconds;
            }
            else total.ReadFailures++;
            Retire(slot);
        }
    }

    /// <summary>Copies a stable snapshot; this method performs no GL calls or waiting.</summary>
    public SurfaceWorkMeasurement Snapshot(SurfaceWorkStage stage)
    {
        var t = totals[(int)stage];
        return new(t.Submitted, t.Collected, t.Skipped, t.ReadFailures, t.Pages,
            t.SubmitMilliseconds, t.GpuMilliseconds, t.CompletionMilliseconds, ImmutableArray.CreateRange(t.Counters));
    }

    /// <summary>Releases the completed ring entry after successful collection or an explicit failure.</summary>
    private void Retire(Slot slot)
    {
        slot.Fence!.Dispose(); slot.Fence = null; PendingCount--;
    }
    #endregion

    #region Lifetime and storage
    /// <summary>Releases outstanding resources without waiting; the owning renderer creates a new collector for a new world.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var slot in slots) slot?.Dispose();
        Array.Clear(slots); PendingCount = 0; active = -1; started = 0;
    }

    /// <summary>Private render-thread aggregate; snapshots never expose its mutable counters.</summary>
    private sealed class Totals
    {
        public long Submitted, Collected, Skipped, ReadFailures, Pages;
        public double SubmitMilliseconds, GpuMilliseconds, CompletionMilliseconds;
        public readonly ulong[] Counters = new ulong[(int)SurfaceWorkCounter.Count];
    }

    /// <summary>One independently fenced diagnostic buffer and its timestamp pair.</summary>
    private sealed class Slot : IDisposable
    {
        public readonly GpuShaderStorageBuffer Buffer = GpuShaderStorageBuffer.Create(debugName: "SurfaceCache.Diagnostics");
        public readonly GpuTimestampQuery Start = GpuTimestampQuery.Create("SurfaceCache.Start");
        public readonly GpuTimestampQuery End = GpuTimestampQuery.Create("SurfaceCache.End");
        public GpuFence? Fence;
        public SurfaceWorkStage Stage;
        public long Started;

        /// <summary>Allocates a fixed counter block, independent of page and ray counts.</summary>
        public Slot() => Buffer.EnsureCapacity((int)SurfaceWorkCounter.Count << 2, growExponentially: false);

        /// <summary>Retires GPU-owned storage and synchronization objects.</summary>
        public void Dispose() { Fence?.Dispose(); Buffer.Dispose(); Start.Dispose(); End.Dispose(); }
    }
    #endregion
}
