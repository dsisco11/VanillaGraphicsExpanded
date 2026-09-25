using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Shares page visits fairly between initialization, direct refresh and indirect tracing.</summary>
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
        public int NextOperation;
        public uint DirectBucket;
        public uint IndirectBucket;
    }

    #region Scheduling
    /// <summary>Selects one operation per admitted page; seed bucket selection remains owned by its completion schedule.</summary>
    public uint Select(uint page, bool seeded, bool published, int batchCount, out uint bucket, int frame = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchCount);
        if (!pages.TryGetValue(page, out var state)) pages[page] = state = new();
        bucket = 0;
        if (!published) return 0;
        // Initial partial work gets its own retry turn without excluding already valid texels
        // from refresh. Completed pages divide their fixed admission budget equally between lights.
        int operation = state.NextOperation % (seeded ? 2 : 3);
        state.NextOperation = (operation + 1) % (seeded ? 2 : 3);
        if (!seeded && operation == 2) return 0;
        if (operation == 0)
        {
            bucket = state.IndirectBucket;
            state.IndirectBucket = (bucket + 1) % (uint)batchCount;
            if (!RetryDelayed(page, bucket, frame)) return 1;
            // Consume the cursor turn, not an indirect trace. Other buckets keep advancing and
            // direct refresh uses this page admission without clearing any displayed samples.
        }
        bucket = state.DirectBucket;
        state.DirectBucket = (bucket + 1) % (uint)batchCount;
        return 4;
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
