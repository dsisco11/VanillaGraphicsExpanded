using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Shares page visits fairly between initialization, direct refresh and indirect tracing.</summary>
internal sealed class LumonSceneLightingRefreshSchedule
{
    private readonly Dictionary<uint, PageState> pages = new();

    /// <summary>Keeps independent bucket cursors so an unresolved bucket cannot monopolize a page.</summary>
    private sealed class PageState
    {
        public int NextOperation;
        public uint DirectBucket;
        public uint IndirectBucket;
    }

    #region Scheduling
    /// <summary>Selects one operation per admitted page; seed bucket selection remains owned by its completion schedule.</summary>
    public uint Select(uint page, bool seeded, bool published, int batchCount, out uint bucket)
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
            return 1;
        }
        bucket = state.DirectBucket;
        state.DirectBucket = (bucket + 1) % (uint)batchCount;
        return 4;
    }

    /// <summary>Retires cursors only when page identity changes, preserving fairness across ordinary dirty notifications.</summary>
    public void Remove(uint page) => pages.Remove(page);

    /// <summary>Retires all cursors after an incompatible resource lifetime or settings change.</summary>
    public void Clear() => pages.Clear();
    #endregion
}
