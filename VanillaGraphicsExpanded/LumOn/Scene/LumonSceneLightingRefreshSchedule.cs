using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Advances independent direct and indirect buckets while bounding exhausted traversal retries.</summary>
internal sealed class LumonSceneLightingRefreshSchedule
{
    private readonly Dictionary<uint, PageState> pages = new();
    public const int ExhaustionRetryFrames = 32, MaximumRetryEntries = 4096;
    private readonly Dictionary<(uint Page, uint Bucket), LinkedListNode<(uint Page, uint Bucket, uint Frame)>> retries = new();
    private readonly LinkedList<(uint Page, uint Bucket, uint Frame)> retryOrder = new();
    internal int RetryCount => retries.Count;

    /// <summary>Keeps independent bucket cursors so an unresolved bucket cannot monopolize a page.</summary>
    private sealed class PageState
    {

        public uint DirectBucket;
        public uint IndirectBucket;
    }

    #region Scheduling
    /// <summary>Advances one explicitly budgeted operation; delayed indirect buckets consume no dispatch credit.</summary>
    public bool TryNext(uint page, bool indirect, int batchCount, int frame, out uint bucket)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchCount);
        if (!pages.TryGetValue(page, out var state)) pages[page] = state = new();
        if (!indirect)
        {
            bucket = state.DirectBucket;
            state.DirectBucket = (bucket + 1) % (uint)batchCount;
            return true;
        }
        // Visit at most one complete bucket sweep, allowing ready siblings to bypass delayed work.
        for (int i = 0; i < batchCount; i++)
        {
            bucket = state.IndirectBucket;
            state.IndirectBucket = (bucket + 1) % (uint)batchCount;
            if (!RetryDelayed(page, bucket, frame)) return true;
        }
        bucket = 0;
        return false;
    }
    /// <summary>Retires cursors only when page identity changes, preserving fairness across ordinary dirty notifications.</summary>
    public void Remove(uint page)
    {
        pages.Remove(page);
        for (var node = retryOrder.First; node != null;)
        {
            var next = node.Next;
            if (node.Value.Page == page)
            {
                retries.Remove((page, node.Value.Bucket));
                retryOrder.Remove(node);
            }
            node = next;
        }
    }

    /// <summary>Retires all cursors after an incompatible resource lifetime or settings change.</summary>
    public void Clear() { pages.Clear(); ClearRetryDelays(); }
    #endregion

    #region Exhausted traversal retries
    /// <summary>Defers a limited bucket with fixed storage; oldest entries yield when the bounded table is full.</summary>
    public void RecordExhaustion(uint page, uint bucket, int frame)
    {
        var key = (page, bucket);
        if (retries.Remove(key, out var previous)) retryOrder.Remove(previous);
        if (retries.Count == MaximumRetryEntries)
        {
            var oldest = retryOrder.First!;
            retries.Remove((oldest.Value.Page, oldest.Value.Bucket));
            retryOrder.RemoveFirst();
        }
        retries.Add(key, retryOrder.AddLast((page, bucket, unchecked((uint)frame))));
    }

    /// <summary>Wakes delayed buckets after geometry changes without altering history or fair bucket cursors.</summary>
    public void ClearRetryDelays() { retries.Clear(); retryOrder.Clear(); }

    /// <summary>Expires delays using unsigned elapsed frames, including signed frame-counter wraparound.</summary>
    private bool RetryDelayed(uint page, uint bucket, int frame)
    {
        var key = (page, bucket);
        if (!retries.TryGetValue(key, out var entry)) return false;
        if (unchecked((uint)frame - entry.Value.Frame) < ExhaustionRetryFrames) return true;
        retries.Remove(key);
        retryOrder.Remove(entry);
        return false;
    }
    #endregion
}
