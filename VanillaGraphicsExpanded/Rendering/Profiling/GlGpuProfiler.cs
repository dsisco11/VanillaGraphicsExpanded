using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Profiling;

public sealed class GlGpuProfiler : IDisposable
{
    private const int FramesInFlight = 16;
    private const int RollingWindow = 120;
    private const int BaselineSamples = 240;

    private static readonly Lazy<GlGpuProfiler> LazyInstance = new(() => new GlGpuProfiler());

    public static GlGpuProfiler Instance => LazyInstance.Value;

    private readonly object gate = new();

    private readonly Dictionary<string, EventState> events = new(StringComparer.Ordinal);
    private readonly Stack<EventToken> stack = new();

    private ICoreClientAPI? capi;
    private int frameIndex;
    private int width;
    private int height;

    public bool Enabled { get; set; } = true;

    public bool EmitDebugGroups { get; set; } = true;

    private GlGpuProfiler() { }

    public void Initialize(ICoreClientAPI capi)
    {
        lock (gate)
        {
            this.capi = capi;
        }
    }

    public void BeginFrame(int width, int height)
    {
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
    }

    public void Collect()
    {
        if (!Enabled)
        {
            return;
        }

        lock (gate)
        {
            CollectInternal();
        }
    }

    private void CollectInternal()
    {
        foreach (var (_, evt) in events)
        {
            evt.CollectResolvedSamples(capi);
        }
    }

    public GlGpuProfilerScope Scope(string name)
    {
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
    }

    internal int BeginEvent(string name)
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

    public bool TryGetStats(string name, out GpuProfileStats stats)
    {
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
    }

    public GpuProfileEntry[] GetSnapshot(
        GpuProfileSort sort = GpuProfileSort.Name,
        string? prefix = null,
        int maxEntries = 128)
    {
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
    }

    public void Dispose()
    {
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
    }

    private readonly record struct EventToken(string Name, int Slot)
    {
        private static int NextId;

        public int Id { get; } = ++NextId;
    }

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

        public EventState(string name)
        {
            this.name = name;
        }

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

        public void MarkPending(int slot)
        {
            pending[slot] = true;
        }

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
                if (deltaNs <= 0)
                {
                    continue;
                }

                float ms = deltaNs / 1_000_000f;
                if (!float.IsFinite(ms) || ms <= 0f)
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
}
