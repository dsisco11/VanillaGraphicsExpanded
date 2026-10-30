using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Profiling;

/// <summary>Provides GPU timing scopes whose measurement state exists only in DEBUG builds.</summary>
public sealed class GlGpuProfiler : IDisposable
{
#if DEBUG
    private const int FramesInFlight = 16;
    private const int RollingWindow = 120;
    private const int BaselineSamples = 240;

#endif

    private static readonly Lazy<GlGpuProfiler> LazyInstance = new(() => new GlGpuProfiler());

    public static GlGpuProfiler Instance => LazyInstance.Value;

#if DEBUG
    private readonly object gate = new();

    private readonly Dictionary<string, EventState> events = new(StringComparer.Ordinal);
    private readonly Stack<EventToken> stack = new();

    private ICoreClientAPI? capi;
    private int frameIndex;
    private int width;
    private int height;

    public bool Enabled { get; set; } = true;

    public bool EmitDebugGroups { get; set; } = true;

#else
    /// <summary>Profiling is unavailable in Release; setting this property enables no measurement work.</summary>
    public bool Enabled { get => false; set { } }
    /// <summary>Debug groups are unavailable through Release profiling scopes.</summary>
    public bool EmitDebugGroups { get => false; set { } }
#endif

    #region Public API
    /// <summary>Associates debug sample reporting with the client logger.</summary>
    [Conditional("DEBUG")]
    public void Initialize(ICoreClientAPI capi)
    {
#if DEBUG
        lock (gate)
        {
            this.capi = capi;
        }
#endif
    }

    /// <summary>Collects completed debug queries and advances the timing frame.</summary>
    [Conditional("DEBUG")]
    public void BeginFrame(int width, int height)
    {
#if DEBUG
        if (!Enabled)
        {
            return;
        }

        lock (gate)
        {
            this.width = width;
            this.height = height;

            CollectInternal();
            frameIndex++;

            // Clear any leftover stack in case a renderer early-returned without disposing.
            // This keeps the profiler resilient in dev builds.
            if (stack.Count > 0)
            {
                stack.Clear();
            }
        }
#endif
    }

    /// <summary>Collects completed debug measurements without advancing the frame.</summary>
    [Conditional("DEBUG")]
    public void Collect()
    {
#if DEBUG
        if (!Enabled)
        {
            return;
        }

        lock (gate)
        {
            CollectInternal();
        }
#endif
    }

    /// <summary>Starts a debug timing scope, or returns an empty value scope in Release.</summary>
    public GlGpuProfilerScope Scope(string name)
    {
#if DEBUG
        if (!Enabled)
        {
            return default;
        }

        lock (gate)
        {
            string fullName = stack.Count == 0
                ? name
                : $"{stack.Peek().Name}/{name}";

            int token = BeginEvent(fullName);
            var group = token != 0 && EmitDebugGroups
                ? GlDebug.Group(fullName)
                : default;

            return new GlGpuProfilerScope(this, token, group);
        }
#else
        return default;
#endif
    }

    /// <summary>Returns available debug samples; Release reports measurement unavailability.</summary>
    public bool TryGetStats(string name, out GpuProfileStats stats)
    {
#if DEBUG
        lock (gate)
        {
            if (events.TryGetValue(name, out var evt))
            {
                stats = evt.GetStats();
                return true;
            }
        }

        stats = default;
        return false;
#else
        stats = default;
        return false;
#endif
    }

    /// <summary>Returns collected debug samples, or an empty snapshot when profiling is unavailable.</summary>
    public GpuProfileEntry[] GetSnapshot(
        GpuProfileSort sort = GpuProfileSort.Name,
        string? prefix = null,
        int maxEntries = 128)
    {
#if DEBUG
        lock (gate)
        {
            if (events.Count == 0)
            {
                return [];
            }

            var list = new List<GpuProfileEntry>(Math.Min(events.Count, maxEntries));
            foreach (var (name, evt) in events)
            {
                if (prefix != null && !name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                list.Add(new GpuProfileEntry(name, evt.GetStats()));
                if (list.Count >= maxEntries)
                {
                    break;
                }
            }

            list.Sort(sort switch
            {
                GpuProfileSort.LastMs => static (a, b) => b.Stats.LastMs.CompareTo(a.Stats.LastMs),
                GpuProfileSort.AvgMs => static (a, b) => b.Stats.AvgMs.CompareTo(a.Stats.AvgMs),
                GpuProfileSort.MaxMs => static (a, b) => b.Stats.MaxMs.CompareTo(a.Stats.MaxMs),
                _ => static (a, b) => string.CompareOrdinal(a.Name, b.Name)
            });

            return list.ToArray();
        }
#else
        return [];
#endif
    }

    /// <summary>Releases debug timing resources; the Release facade owns no GPU state.</summary>
    [Conditional("DEBUG")]
    public void Dispose()
    {
#if DEBUG
        lock (gate)
        {
            foreach (var (_, evt) in events)
            {
                evt.Dispose();
            }

            events.Clear();
            stack.Clear();
            capi = null;
        }
#endif
    }

#if DEBUG
    /// <summary>Completes the matching active timing query.</summary>
    [Conditional("DEBUG")]
    internal void EndEvent(int tokenId)
    {
        if (!Enabled || tokenId == 0)
        {
            return;
        }

        if (stack.Count == 0)
        {
            return;
        }

        var token = stack.Pop();
        if (token.Id != tokenId)
        {
            // Stack mismatch: best-effort recovery.
            stack.Clear();
            return;
        }

        if (!events.TryGetValue(token.Name, out var evt))
        {
            return;
        }

        if (!evt.TryEnd(token.Slot, out var endQuery))
        {
            return;
        }

        endQuery.Issue();
        evt.MarkPending(token.Slot);
    }

#endif

    /// <summary>Implements deterministic cleanup through the same debug-only disposal operation.</summary>
    void IDisposable.Dispose() => Dispose();

    #endregion

    #region Private
    /// <summary>Creates the process-wide facade without allocating Release measurement state.</summary>
    private GlGpuProfiler() { }

#if DEBUG
    /// <summary>Collects available query results for each retained debug event.</summary>
    private void CollectInternal()
    {
        foreach (var (_, evt) in events)
        {
            evt.CollectResolvedSamples(capi);
        }
    }

    /// <summary>Reserves and issues the start query for one named event.</summary>
    private int BeginEvent(string name)
    {
        if (!Enabled)
        {
            return 0;
        }

        if (!events.TryGetValue(name, out var evt))
        {
            evt = new EventState(name);
            events[name] = evt;
        }

        evt.EnsureResolution(width, height);

        int slot = frameIndex % FramesInFlight;
        if (!evt.TryBegin(slot, out var beginQuery))
        {
            return 0;
        }

        beginQuery.Issue();

        var token = new EventToken(name, slot);
        stack.Push(token);
        return token.Id;
    }

    /// <summary>Retains the identity of one active debug query interval.</summary>
    private readonly record struct EventToken(string Name, int Slot)
    {
        private static int NextId;

        public int Id { get; } = ++NextId;
    }

    /// <summary>Owns timestamp queries and rolling statistics for one debug event.</summary>
    private sealed class EventState : IDisposable
    {
        private readonly string name;

        private readonly GpuTimestampQuery?[] beginQueries = new GpuTimestampQuery?[FramesInFlight];
        private readonly GpuTimestampQuery?[] endQueries = new GpuTimestampQuery?[FramesInFlight];
        private readonly bool[] pending = new bool[FramesInFlight];

        private readonly float[] window = new float[RollingWindow];
        private int windowCount;
        private int windowIndex;

        private float lastMs;
        private float windowSum;
        private float windowMin = float.PositiveInfinity;
        private float windowMax = float.NegativeInfinity;

        private int width;
        private int height;

        private int baselineCount;
        private float baselineSum;
        private float baselineMin = float.PositiveInfinity;
        private float baselineMax = float.NegativeInfinity;
        private bool baselineLogged;

        /// <summary>Initializes the named debug measurement state.</summary>
        public EventState(string name)
        {
            this.name = name;
        }

        /// <summary>Resets baseline accumulation when the output extent changes.</summary>
        public void EnsureResolution(int width, int height)
        {
            if (this.width == width && this.height == height)
            {
                return;
            }

            this.width = width;
            this.height = height;

            baselineCount = 0;
            baselineSum = 0f;
            baselineMin = float.PositiveInfinity;
            baselineMax = float.NegativeInfinity;
            baselineLogged = false;
        }

        /// <summary>Reserves a timestamp query when its frame slot has retired.</summary>
        public bool TryBegin(int slot, out GpuTimestampQuery query)
        {
            if (pending[slot])
            {
                query = null!;
                return false;
            }

            query = beginQueries[slot] ?? GpuTimestampQuery.Create(debugName: $"{name}.Begin[{slot}]");
            if (!query.IsValid)
            {
                query.Dispose();
                query = GpuTimestampQuery.Create(debugName: $"{name}.Begin[{slot}]");
            }

            beginQueries[slot] = query;
            return true;
        }

        /// <summary>Gets the matching end timestamp query for the frame slot.</summary>
        public bool TryEnd(int slot, out GpuTimestampQuery query)
        {
            query = endQueries[slot] ?? GpuTimestampQuery.Create(debugName: $"{name}.End[{slot}]");
            if (!query.IsValid)
            {
                query.Dispose();
                query = GpuTimestampQuery.Create(debugName: $"{name}.End[{slot}]");
            }

            endQueries[slot] = query;
            return true;
        }

        /// <summary>Marks a completed interval for later asynchronous readback.</summary>
        public void MarkPending(int slot)
        {
            pending[slot] = true;
        }

        /// <summary>Collects ready timestamp pairs without waiting for the GPU.</summary>
        public void CollectResolvedSamples(ICoreClientAPI? capi)
        {
            for (int slot = 0; slot < FramesInFlight; slot++)
            {
                if (!pending[slot])
                {
                    continue;
                }

                var begin = beginQueries[slot];
                var end = endQueries[slot];
                if (begin is null || end is null || !begin.IsValid || !end.IsValid)
                {
                    pending[slot] = false;
                    continue;
                }

                if (!begin.IsResultAvailable() || !end.IsResultAvailable())
                {
                    continue;
                }

                long beginNs = begin.GetResultNanoseconds();
                long endNs = end.GetResultNanoseconds();

                pending[slot] = false;

                long deltaNs = endNs - beginNs;
                // Two completed timestamps can share a clock tick. Retain a zero-duration
                // interval as a sample; only reversed time is invalid.
                if (deltaNs < 0)
                {
                    continue;
                }

                float ms = deltaNs / 1_000_000f;
                if (!float.IsFinite(ms) || ms < 0f)
                {
                    continue;
                }

                AddSample(ms);

                if (capi != null)
                {
                    AccumulateBaseline(capi, ms);
                }
            }
        }

        /// <summary>Updates the bounded rolling window and its extrema.</summary>
        private void AddSample(float ms)
        {
            lastMs = ms;

            if (windowCount < RollingWindow)
            {
                window[windowCount++] = ms;
                windowSum += ms;
            }
            else
            {
                float old = window[windowIndex];
                window[windowIndex] = ms;
                windowSum += ms - old;
                windowIndex = (windowIndex + 1) % RollingWindow;
            }

            // Window min/max recompute is cheap at our sizes, and avoids tricky incremental invalidation.
            windowMin = float.PositiveInfinity;
            windowMax = float.NegativeInfinity;
            for (int i = 0; i < windowCount; i++)
            {
                float v = window[i];
                windowMin = Math.Min(windowMin, v);
                windowMax = Math.Max(windowMax, v);
            }
        }

        /// <summary>Logs one completed debug baseline for the current extent.</summary>
        private void AccumulateBaseline(ICoreClientAPI capi, float ms)
        {
            if (baselineLogged)
            {
                return;
            }

            baselineCount++;
            baselineSum += ms;
            baselineMin = Math.Min(baselineMin, ms);
            baselineMax = Math.Max(baselineMax, ms);

            if (baselineCount >= BaselineSamples)
            {
                float avg = baselineSum / Math.Max(1, baselineCount);
                capi.Logger.Notification(
                    $"[VGE] GPU Profiler baseline: {name} avg={avg:0.###}ms min={baselineMin:0.###}ms max={baselineMax:0.###}ms over {baselineCount} frames @ {width}x{height}");
                baselineLogged = true;
            }
        }

        /// <summary>Builds the current available measurement snapshot.</summary>
        public GpuProfileStats GetStats()
        {
            float avg = windowCount > 0 ? windowSum / windowCount : 0f;
            float min = windowCount > 0 ? windowMin : 0f;
            float max = windowCount > 0 ? windowMax : 0f;

            return new GpuProfileStats(
                LastMs: lastMs,
                AvgMs: avg,
                MinMs: min,
                MaxMs: max,
                SampleCount: windowCount,
                Width: width,
                Height: height);
        }

        /// <summary>Retires all owned debug timestamp queries.</summary>
        public void Dispose()
        {
            for (int i = 0; i < FramesInFlight; i++)
            {
                beginQueries[i]?.Dispose();
                beginQueries[i] = null;

                endQueries[i]?.Dispose();
                endQueries[i] = null;

                pending[i] = false;
            }
        }
    }
#endif
    #endregion
}
