using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Retains completed seed buckets while fairly retrying only unresolved buckets of the same page identity.</summary>
internal sealed class LumonSceneRelightBatchSchedule
{
    private readonly Dictionary<ulong, State> pages = new();

    /// <summary>Tracks initialized buckets independently so a failure cannot erase unrelated progress.</summary>
    private sealed class State(int count)
    {
        public readonly bool[] Complete = new bool[count];
        public int Next;
        public int Selected;
        public int Remaining = count;
    }

    #region Scheduling
    /// <summary>Selects the next unresolved bucket without restarting successful work after a failure.</summary>
    public uint Next(ulong page, int count)
    {
        if (count <= 1) return 0;
        if (!pages.TryGetValue(page, out var state) || state.Complete.Length != count)
            pages[page] = state = new(count);
        while (state.Complete[state.Next]) state.Next = (state.Next + 1) % count;
        state.Selected = state.Next;
        state.Next = (state.Next + 1) % count;
        return (uint)state.Selected;
    }

    /// <summary>Finishes a page only after every bucket succeeds at least once within its current identity.</summary>
    public bool Complete(ulong page, int count, bool succeeded)
    {
        if (count <= 1) return succeeded;
        if (!pages.TryGetValue(page, out var state)) return false;
        if (succeeded && !state.Complete[state.Selected])
        {
            state.Complete[state.Selected] = true;
            state.Remaining--;
        }
        if (state.Remaining != 0) return false;
        pages.Remove(page);
        return true;
    }

    /// <summary>Discards schedules after a configuration, history or world-generation change.</summary>
    public void Clear() => pages.Clear();
    /// <summary>Discards only the initialization progress of an invalidated or reassigned page.</summary>
    public void Remove(ulong page) => pages.Remove(page);
    #endregion
}
