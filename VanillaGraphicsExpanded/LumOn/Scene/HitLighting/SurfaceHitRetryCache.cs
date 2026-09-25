using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene.HitLighting;

/// <summary>Bounds retained geometry and rotates ready lookup work without blocking new direct seeds.</summary>
internal sealed class SurfaceHitRetryCache
{
    public const int Capacity = 32, MaximumAgeFrames = 256;
    private readonly List<SurfaceHitRetryEntry> entries = new(Capacity);
    private int cursor;
    public IReadOnlyList<SurfaceHitRetryEntry> Entries => entries;
    public int Count => entries.Count;

    #region Admission and scheduling
    /// <summary>Rejects duplicates and saturation without displacing in-flight lookup ownership.</summary>
    public bool TryAdd(SurfaceHitRetryEntry entry)
    {
        if (entries.Count == Capacity) return false;
        foreach (var existing in entries)
            if (existing.Request.Page == entry.Request.Page && existing.Request.Slot == entry.Request.Slot &&
                existing.Request.Patch == entry.Request.Patch && existing.Request.Linear == entry.Request.Linear) return false;
        entries.Add(entry);
        return true;
    }

    /// <summary>Retires obsolete or expired samples, restoring their ordinary tracing eligibility.</summary>
    public void Prune(int frame, Func<SurfaceHitRetryEntry, bool> isCurrent)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (unchecked((uint)frame - entries[i].CreatedFrame) >= MaximumAgeFrames || !isCurrent(entries[i]))
            {
                entries.RemoveAt(i);
                if (i < cursor) cursor--;
            }
    }

    /// <summary>Returns at most one ready sample per call, advancing fairly past unchanged dependencies.</summary>
    public SurfaceHitRetryEntry? Select()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            cursor %= entries.Count;
            var entry = entries[cursor++];
            if (!entry.Pending && entry.NeedsQuery) return entry;
        }
        return null;
    }

    /// <summary>Releases a completed or invalidated sample without touching its displayed lighting.</summary>
    public void Remove(SurfaceHitRetryEntry entry)
    {
        int index = entries.IndexOf(entry);
        if (index < 0) return;
        entries.RemoveAt(index);
        // Preserve the next entry's turn when a completed predecessor leaves the compact list.
        if (index < cursor) cursor--;
    }

    /// <summary>Retires all descriptors after a resource lifetime change.</summary>
    public void Clear() { entries.Clear(); cursor = 0; }
    #endregion
}
