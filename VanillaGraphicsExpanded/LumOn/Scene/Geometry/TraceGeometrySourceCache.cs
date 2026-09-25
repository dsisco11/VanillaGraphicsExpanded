using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Bounded coalesced source requests with pinned unconsumed cells and cancellation credit retention.</summary>
internal sealed class TraceGeometrySourceCache : IDisposable
{
    /// <summary>One immutable source revision and the cells still waiting to detach their payload.</summary>
    private sealed class Entry
    {
        public required int Version;
        public required Task<TraceGeometryChunk?> Task;
        public required CancellationTokenSource Cancellation;
        public readonly HashSet<PartitionCoordinate> Pending = new();
        public long Used;
    }
    private readonly Func<ChunkKey, int, CancellationToken, Task<TraceGeometryChunk?>> load;
    private readonly Func<ChunkKey, int> version;
    private readonly Func<ChunkKey, bool> available;
    private readonly Dictionary<ChunkKey, Entry> entries = new();
    private readonly Dictionary<ChunkKey, (long At, int Count)> retries = new();
    private readonly List<Task> cancelled = new();
    private long frame;
    public long SourceReads { get; private set; }
    public int InFlight => entries.Values.Count(e => !e.Task.IsCompleted) + cancelled.Count;
    public long SnapshotBytes => entries.Values.Where(e => e.Task.IsCompletedSuccessfully && e.Task.Result != null).Sum(e => e.Task.Result!.EstimatedBytes);

    /// <summary>Injects an existing worker executor and loaded-source dependency observations.</summary>
    public TraceGeometrySourceCache(Func<ChunkKey, int, CancellationToken, Task<TraceGeometryChunk?>> load,
        Func<ChunkKey, int> version, Func<ChunkKey, bool> available)
    { this.load = load; this.version = version; this.available = available; }

    #region Capture selection
    /// <summary>Coalesces nearby demand under a per-frame admission ceiling and eight-source lifetime bound.</summary>
    public void Update(IReadOnlyList<PartitionCoordinate> demand, TraceGeometryCoverage coverage,
        IReadOnlySet<PartitionCoordinate>? waiting = null, Func<PartitionCoordinate, Action?>? authorize = null,
        int maxCapturesPerFrame = 2)
    {
        if (maxCapturesPerFrame is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(maxCapturesPerFrame));
        frame++; cancelled.RemoveAll(t => t.IsCompleted);
        var groups = demand.GroupBy(c => TraceGeometryCoverage.SourceChunk(c)).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var key in retries.Keys.Where(key => !groups.ContainsKey(key)).ToArray()) retries.Remove(key);
        foreach (var pair in entries.ToArray())
        {
            if (!groups.ContainsKey(pair.Key) || !IsCurrent(pair.Key, pair.Value.Version)) { Remove(pair.Key); retries.Remove(pair.Key); continue; }
            pair.Value.Pending.IntersectWith(groups[pair.Key]);
            // Newly requested subcells must pin an existing snapshot before admission can evict it.
            pair.Value.Pending.UnionWith(groups[pair.Key].Where(c => waiting == null || waiting.Contains(c)));
            if (pair.Value.Task.IsCompleted && (!pair.Value.Task.IsCompletedSuccessfully || pair.Value.Task.Result == null))
            {
                int count = retries.TryGetValue(pair.Key, out var old) ? old.Count + 1 : 1;
                retries[pair.Key] = (frame + (1L << Math.Min(6, count - 1)), count); Remove(pair.Key);
            }
        }
        var missing = groups.Where(g => !entries.ContainsKey(g.Key) && (waiting == null || g.Value.Any(waiting.Contains))).ToArray();
        var eligible = missing.Where(g => Eligible(g.Key)).OrderBy(g => g.Value.Min(c => coverage.DistanceSquared(c))).ToArray();
        var near = eligible.Where(g => g.Value.Any(c => coverage.IsNear(c))).ToArray();
        var surface = eligible.Where(g => !g.Value.Any(c => coverage.IsNear(c))).ToArray();
        int issued = 0;
        // Preserve both consumers' opportunities. With one admission, alternate the preferred consumer.
        var ordered = maxCapturesPerFrame == 1 && (frame & 1) == 0
            ? near.Take(1).Concat(surface).Concat(near.Skip(1))
            : surface.Take(1).Concat(near).Concat(surface.Skip(1));
        foreach (var group in ordered)
        {
            if (issued >= maxCapturesPerFrame || InFlight >= 8) break;
            if (retries.TryGetValue(group.Key, out var retry) && retry.At > frame) continue;
            if (!available(group.Key)) { retries[group.Key] = (frame + 64, 6); continue; }
            if (entries.Count >= 8)
            {
                var victim = entries.Where(e => e.Value.Task.IsCompleted && e.Value.Pending.Count == 0).OrderBy(e => e.Value.Used).FirstOrDefault();
                if (victim.Value == null) break;
                Remove(victim.Key);
            }
            Action? acknowledge = authorize?.Invoke(group.Value.First(c => waiting == null || waiting.Contains(c)));
            if (authorize != null && acknowledge == null) continue;
            int revision = version(group.Key);
            var cancellation = new CancellationTokenSource();
            Task<TraceGeometryChunk?> task;
            try { task = load(group.Key, revision, cancellation.Token); }
            catch { cancellation.Dispose(); acknowledge?.Invoke(); throw; }
            if (acknowledge != null)
                _ = task.ContinueWith(_ => acknowledge(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var entry = new Entry { Version = revision, Cancellation = cancellation, Task = task, Used = frame };
            entry.Pending.UnionWith(group.Value.Where(c => waiting == null || waiting.Contains(c))); entries.Add(group.Key, entry); issued++; SourceReads++;
        }
    }

    /// <summary>Skips unavailable sources before reserving either consumer's service opportunity.</summary>
    private bool Eligible(ChunkKey key)
    {
        if (retries.TryGetValue(key, out var retry) && retry.At > frame) return false;
        if (available(key)) return true;
        retries[key] = (frame + 64, 6); return false;
    }

    /// <summary>Checks capture completion without unpinning or allocating a publication payload.</summary>
    public bool HasSnapshot(in PartitionCoordinate coordinate)
    {
        var key = TraceGeometryCoverage.SourceChunk(coordinate);
        return entries.TryGetValue(key, out var entry) && entry.Task.IsCompletedSuccessfully && entry.Task.Result != null && IsCurrent(key, entry.Version);
    }

    /// <summary>Returns one immutable publication payload from an already completed worker source.</summary>
    public bool TryExtract(in PartitionCoordinate coordinate, out TraceGeometryChunk? chunk, out TraceGeometryCell? cell)
    {
        chunk = null; cell = null;
        ChunkKey key = TraceGeometryCoverage.SourceChunk(coordinate);
        if (!entries.TryGetValue(key, out var entry) || !entry.Task.IsCompletedSuccessfully || entry.Task.Result == null || !IsCurrent(key, entry.Version)) return false;
        chunk = entry.Task.Result; cell = chunk.Extract(coordinate); entry.Pending.Remove(coordinate); entry.Used = frame;
        return true;
    }

    /// <summary>Validates loaded identity/version even after its cached snapshot has been evicted.</summary>
    public bool IsCurrent(ChunkKey key, int revision) => available(key) && version(key) == revision;

    /// <summary>Allows explicit edits to reset missing-source retry delays.</summary>
    public void Dirty(ChunkKey key) { retries.Remove(key); Remove(key); }
    #endregion

    #region Retirement
    /// <summary>Cancels without freeing worker credit until the task actually acknowledges completion.</summary>
    private void Remove(ChunkKey key)
    {
        if (!entries.Remove(key, out var entry)) return;
        entry.Cancellation.Cancel();
        if (!entry.Task.IsCompleted) cancelled.Add(entry.Task);
        _ = entry.Task.ContinueWith(task => { _ = task.Exception; entry.Cancellation.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Ends every pending request without blocking the render thread.</summary>
    public void Dispose()
    { foreach (var key in entries.Keys.ToArray()) Remove(key); retries.Clear(); }
    #endregion
}
