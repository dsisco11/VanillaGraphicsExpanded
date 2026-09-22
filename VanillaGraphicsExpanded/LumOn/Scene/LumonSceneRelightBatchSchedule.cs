using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Preserves fair texel-bucket progress while requiring a complete successful sweep before page completion.</summary>
internal sealed class LumonSceneRelightBatchSchedule
{
    private readonly Dictionary<ulong, State> pages = new();
    /// <summary>Tracks the next bucket and remaining consecutive successful buckets.</summary>
    private readonly record struct State(int Next, int Remaining);

    #region Scheduling
    /// <summary>Advances the cursor independently of success so an unavailable bucket cannot starve later texels.</summary>
    public uint Next(ulong page, int count)
    {
        if (count <= 1) return 0;
        if (!pages.TryGetValue(page, out var state)) state = new(0, count);
        pages[page] = new((state.Next + 1) % count, state.Remaining);
        return (uint)state.Next;
    }

    /// <summary>Requires a fresh full sweep after failure while retaining the already-advanced round-robin cursor.</summary>
    public bool Complete(ulong page, int count, bool succeeded)
    {
        if (count <= 1) return succeeded;
        if (!pages.TryGetValue(page, out var state)) state = new(0, count);
        int remaining = succeeded ? state.Remaining - 1 : count;
        if (remaining <= 0) { pages.Remove(page); return true; }
        pages[page] = new(state.Next, remaining);
        return false;
    }

    /// <summary>Discards schedules after a configuration, history or world-generation change.</summary>
    public void Clear() => pages.Clear();
    #endregion
}
