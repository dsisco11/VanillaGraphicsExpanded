using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Bounded coalescing cache shared by all subcells of a source chunk.</summary>
internal sealed class NearFieldChunkCache : INearFieldChunkSource, IDisposable
{
    /// <summary>One source request retains cancellation ownership until all workers acknowledge it.</summary>
    private sealed record Entry(int Version, CancellationTokenSource Cancellation, Task<NearFieldChunkSnapshot?> Task, long Frame);
    private readonly Func<ChunkKey, int, CancellationToken, Task<NearFieldChunkSnapshot?>> load;
    private readonly Func<ChunkKey, int> version;
    private readonly Func<ChunkKey, bool> available;
    private readonly Dictionary<ChunkKey, Entry> entries = new();
    private readonly List<Task> cancelled = new();
    private readonly int maximumInFlight, capturesPerFrame;
    private int captures;
    private long frame;
    public int SourceReads { get; private set; }

    /// <summary>Injects asynchronous capture through the existing executor and source dependency observations.</summary>
    public NearFieldChunkCache(Func<ChunkKey, int, CancellationToken, Task<NearFieldChunkSnapshot?>> load,
        Func<ChunkKey, int> version, Func<ChunkKey, bool> available, int maximumInFlight = 8, int capturesPerFrame = 2)
    {
        if (maximumInFlight <= 0 || capturesPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maximumInFlight));
        this.load = load; this.version = version; this.available = available;
        this.maximumInFlight = maximumInFlight; this.capturesPerFrame = capturesPerFrame;
    }

    #region Frame and lifetime
    /// <summary>Bounds cache residency by the GPU window and resets separate source-read service credits.</summary>
    public void BeginFrame(in PartitionCellRange cells)
    {
        frame++; captures = 0;
        cancelled.RemoveAll(t => t.IsCompleted);
        foreach (var entry in entries.ToArray())
        {
            entry.Key.Decode(out int x, out int y, out int z);
            if (cells.Empty || x < (cells.Min.X >> 1) || y < (cells.Min.Y >> 1) || z < (cells.Min.Z >> 1) ||
                x > ((cells.End.X - 1) >> 1) || y > ((cells.End.Y - 1) >> 1) || z > ((cells.End.Z - 1) >> 1) ||
                !IsCurrent(entry.Key, entry.Value.Version)) Remove(entry.Key, entry.Value);
        }
    }

    /// <summary>Cancels all source work without waiting on game-thread tasks during teardown.</summary>
    public void Dispose()
    {
        foreach (var entry in entries.ToArray()) Remove(entry.Key, entry.Value);
    }

    /// <summary>Retains cancelled workers in the capacity accounting until they stop.</summary>
    private void Remove(ChunkKey key, Entry entry)
    {
        entries.Remove(key);
        entry.Cancellation.Cancel();
        entry.Cancellation.Dispose();
        if (!entry.Task.IsCompleted) cancelled.Add(entry.Task);
    }
    #endregion

    #region Source queries
    /// <summary>Shares an existing request/result; absent source data is a retryable condition.</summary>
    public bool TryGet(ChunkKey key, out NearFieldChunkSnapshot? snapshot)
    {
        snapshot = null;
        if (!available(key))
        {
            if (entries.TryGetValue(key, out Entry? unavailable)) Remove(key, unavailable);
            return false;
        }
        int current = version(key);
        if (entries.TryGetValue(key, out Entry? entry))
        {
            if (entry.Version != current) Remove(key, entry);
            else if (!entry.Task.IsCompleted) return false;
            else if (entry.Task.IsCompletedSuccessfully && entry.Task.Result is { } result && result.Key == key && result.Version == current)
            {
                snapshot = result;
                return true;
            }
            else if (entry.Frame == frame) return false;
            else Remove(key, entry);
        }
        if (captures >= capturesPerFrame || entries.Values.Count(e => !e.Task.IsCompleted) + cancelled.Count >= maximumInFlight) return false;
        var cancellation = new CancellationTokenSource();
        captures++; SourceReads++;
        Task<NearFieldChunkSnapshot?> task = load(key, current, cancellation.Token);
        entries.Add(key, new(current, cancellation, task, frame));
        if (task.IsCompletedSuccessfully && task.Result is { } completed && completed.Key == key && completed.Version == current && IsCurrent(key, completed.Version)) snapshot = completed;
        return snapshot != null;
    }

    /// <summary>Requires both a matching source revision and a still-loaded chunk.</summary>
    public bool IsCurrent(ChunkKey key, int expected) => available(key) && version(key) == expected;
    #endregion
}
