using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Preserves lighting progress for unchanged page identities across residency changes.</summary>
internal sealed partial class LumonSceneRelightUpdateRenderer
{
    private readonly Dictionary<uint, ushort> pageGenerations = new();

    #region Page residency
    /// <summary>Reconciles page ownership and capture readiness without resetting unrelated lighting work.</summary>
    private void SynchronizePages(IReadOnlyDictionary<uint, ulong> mapping, LumonScenePageTableEntry[] mirror, bool selectCandidates)
    {
        var generations = feedback.LightingSlotGenerations;
        foreach (var pair in identities.ToArray())
        {
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Value);
            if (mapping.TryGetValue(pair.Key, out ulong key) && key == pair.Value &&
                slot < generations.Length && pageGenerations[pair.Key] == generations[(int)slot]) continue;
            batches.Remove(pair.Value);
            seeded.Remove(pair.Key);
            readiness[pair.Key] = 0;
            readyBuffer!.UploadSubData<uint>(readiness.AsSpan((int)pair.Key, 1), checked((int)((long)pair.Key << 2)), 4);
            identities.Remove(pair.Key);
            pageGenerations.Remove(pair.Key);
        }

        var eligible = selectCandidates ? new List<uint>(mapping.Count) : null;
        foreach (var pair in mapping)
        {
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Value);
            if (slot >= generations.Length) continue;
            identities[pair.Key] = pair.Value;
            pageGenerations[pair.Key] = generations[(int)slot];
            int index = checked((int)slot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk +
                (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(pair.Value));
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(mirror[index]);
            if ((flags & (LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.Capturing)) != 0)
            {
                // Recapture replaces this page's inputs; no other page loses its completed sweep.
                batches.Remove(pair.Value);
                if (seeded.Remove(pair.Key))
                {
                    readiness[pair.Key] = 0;
                    readyBuffer!.UploadSubData<uint>(readiness.AsSpan((int)pair.Key, 1), checked((int)((long)pair.Key << 2)), 4);
                }
                continue;
            }
            eligible?.Add(pair.Key);
        }
        // Uncaptured pages must not consume the small GPU relight budget.
        if (eligible != null)
        {
            eligible.Sort();
            candidates = eligible.ToArray();
            cursor = candidates.Length == 0 ? 0 : cursor % candidates.Length;
        }
        published = seeded.Count != 0;
    }
    #endregion
}
