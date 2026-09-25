using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Tracks pending page age from observation until completion, bounded by physical page identities.</summary>
internal sealed class SurfacePageProgress
{
    private readonly Dictionary<uint, Entry> pending = new();
    public int PendingCount => pending.Count;
    public long Completed { get; private set; }
    public long CompletionMilliseconds { get; private set; }

    #region Progress
    /// <summary>Starts an observation once per current page identity; retries do not reset its age.</summary>
    public void Observe(uint page, ulong key, ushort generation, long now)
    {
        if (!pending.TryGetValue(page, out var entry) || entry.Key != key || entry.Generation != generation)
            pending[page] = new(key, generation, now);
    }

    /// <summary>Records successful completion latency, excluding pages that were never observed pending.</summary>
    public void Complete(uint page, long now)
    {
        if (!pending.Remove(page, out var entry)) return;
        Completed++; CompletionMilliseconds += Math.Max(0, now - entry.Started);
    }

    /// <summary>Forgets invalidated identities without counting their retirement as successful completion.</summary>
    public void Remove(uint page) => pending.Remove(page);

    /// <summary>Prunes evicted or reassigned pages before reporting; the callback must verify slot generation too.</summary>
    public void Prune(Func<uint, ulong, ushort, bool> current)
    {
        foreach (var pair in pending.ToArray())
            if (!current(pair.Key, pair.Value.Key, pair.Value.Generation)) pending.Remove(pair.Key);
    }

    /// <summary>Reports the oldest unresolved observation, not an estimate of full lighting convergence.</summary>
    public long OldestMilliseconds(long now)
    {
        long oldest = 0;
        foreach (var entry in pending.Values) oldest = Math.Max(oldest, now - entry.Started);
        return oldest;
    }

    /// <summary>Starts a new measurement lifetime on world leave.</summary>
    public void Clear() { pending.Clear(); Completed = CompletionMilliseconds = 0; }
    #endregion

    /// <summary>Includes virtual identity and slot generation so physical reuse cannot inherit queue age.</summary>
    private readonly record struct Entry(ulong Key, ushort Generation, long Started);
}
