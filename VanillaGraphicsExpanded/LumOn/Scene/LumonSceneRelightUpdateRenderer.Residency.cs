using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Preserves lighting progress for unchanged page identities across residency changes.</summary>
internal sealed partial class LumonSceneRelightUpdateRenderer
{
    private readonly Dictionary<uint, ushort> pageGenerations = new();
    private readonly Dictionary<uint, long> pageCaptureRevisions = new();

    #region Page residency
    /// <summary>Reconciles page ownership and capture readiness without resetting unrelated lighting work.</summary>
    private void SynchronizePages(IReadOnlyDictionary<uint, ulong> mapping, LumonScenePageTableEntry[] mirror, bool selectCandidates)
    {
        var generations = feedback.LightingSlotGenerations;
        foreach (var pair in identities.ToArray())
        {
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Value);
            if (mapping.TryGetValue(pair.Key, out ulong key) && key == pair.Value &&
                slot < generations.Length && pageGenerations[pair.Key] == generations[(int)slot] &&
                pageCaptureRevisions[pair.Key] == feedback.GetCaptureRevision(pair.Key)) continue;
            batches.Remove(pair.Value);
            seeded.Remove(pair.Key); initialized.Remove(pair.Key); publishedPages.Remove(pair.Key);
            refreshSchedule.Remove(pair.Key);
            readiness[pair.Key] = 0;
            readyBuffer!.UploadSubData<uint>(readiness.AsSpan((int)pair.Key, 1), checked((int)((long)pair.Key << 2)), 4);
            identities.Remove(pair.Key);
            pageGenerations.Remove(pair.Key);
            pageCaptureRevisions.Remove(pair.Key);
        }

        var eligible = selectCandidates ? new List<uint>(mapping.Count) : null;
        foreach (var pair in mapping)
        {
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Value);
            if (slot >= generations.Length) continue;
            identities[pair.Key] = pair.Value;
            pageGenerations[pair.Key] = generations[(int)slot];
            pageCaptureRevisions[pair.Key] = feedback.GetCaptureRevision(pair.Key);
            int index = checked((int)slot * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk +
                (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(pair.Value));
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(mirror[index]);
            if ((flags & (LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.Capturing)) != 0)
            {
                // Recapture replaces this page's inputs; no other page loses its completed sweep.
                batches.Remove(pair.Value);
                seeded.Remove(pair.Key); initialized.Remove(pair.Key); refreshSchedule.Remove(pair.Key);
                if (publishedPages.Remove(pair.Key))
                {
                    readiness[pair.Key] = 0;
                    readyBuffer!.UploadSubData<uint>(readiness.AsSpan((int)pair.Key, 1), checked((int)((long)pair.Key << 2)), 4);
                }
                continue;
            }
            // Unknown source identity temporarily hides the tile without losing its last
            // coherent lighting or successful scheduling work. Exact identity recovery restores it.
            bool available = feedback.IsCaptureAvailable(pair.Key);
            uint expectedReady = available && publishedPages.Contains(pair.Key) ? 1u : 0u;
            if (readiness[pair.Key] != expectedReady)
            {
                readiness[pair.Key] = expectedReady;
                readyBuffer!.UploadSubData<uint>(readiness.AsSpan((int)pair.Key, 1), checked((int)((long)pair.Key << 2)), 4);
            }
            if (!available) continue;
            eligible?.Add(pair.Key);
        }
        // Uncaptured pages must not consume the small GPU relight budget.
        if (eligible != null)
        {
            eligible.Sort();
            candidates = eligible.ToArray();
        }
        published = publishedPages.Count != 0;
    }
    #endregion
}
