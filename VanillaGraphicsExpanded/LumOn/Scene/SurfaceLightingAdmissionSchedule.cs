using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Reserves fair identity-order turns alongside visible/near priority within one lighting allocation.</summary>
internal sealed class SurfaceLightingAdmissionSchedule
{
    private uint lastPage;
    private bool priorityTurn;
    private readonly HashSet<uint> selected = new();

    #region Admission
    /// <summary>Admits each eligible page at most once, without charging deferred candidates against the budget.</summary>
    public void Select(uint[] identities, uint[] priority, int budget, Func<uint, bool> admit)
    {
        if (budget <= 0 || identities.Length == 0) return;
        selected.Clear();
        // A one-slot allocation alternates priority and fairness; larger allocations reserve half for fairness.
        int fair = budget == 1 ? (priorityTurn ? 0 : 1) : (budget + 1) >> 1;
        priorityTurn = !priorityTurn;
        int cursor = Array.BinarySearch(identities, lastPage);
        cursor = cursor >= 0 ? cursor + 1 : ~cursor;
        for (int i = 0; i < identities.Length && selected.Count < fair; i++)
        {
            uint page = identities[(cursor + i) % identities.Length];
            lastPage = page;
            if (admit(page)) selected.Add(page);
        }
        foreach (uint page in priority)
        {
            if (selected.Count >= budget) break;
            if (!selected.Contains(page) && admit(page)) selected.Add(page);
        }
    }

    /// <summary>Resets identity-order ownership on world or resource replacement.</summary>
    public void Clear() { lastPage = 0; priorityTurn = false; selected.Clear(); }
    #endregion
}
