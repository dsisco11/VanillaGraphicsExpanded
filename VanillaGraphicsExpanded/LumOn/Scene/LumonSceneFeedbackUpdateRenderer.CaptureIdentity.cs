using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Separates capture identity from lighting freshness and temporary source availability.</summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer
{
    /// <summary>Retains exact inputs and ownership for one successfully captured physical page.</summary>
    private sealed record CaptureIdentity(ulong Key, ushort Generation, uint PatchId, long Revision, ImmutableArray<uint> Voxels);

    private readonly Dictionary<uint, CaptureIdentity> captureIdentities = new();
    private readonly HashSet<uint> unavailableCaptures = new();
    private long nextCaptureRevision;
    private long checkedGeometryRevision = -1;

    #region Capture validity
    /// <summary>Returns the captured content revision independently of ordinary lighting dirtiness.</summary>
    internal long GetCaptureRevision(uint page) => captureIdentities.TryGetValue(page, out var identity) ? identity.Revision : -1;

    /// <summary>Allows retained lighting only while its captured source identity is verified.</summary>
    internal bool IsCaptureAvailable(uint page) => captureIdentities.ContainsKey(page) && !unavailableCaptures.Contains(page);

    /// <summary>Records exact inputs only after GPU capture completion and matching current ownership.</summary>
    private bool RecordCaptureIdentity(in LumonSceneCaptureWorkGpu work, TraceGeometryGpuScene? scene)
    {
        uint slot = work.ChunkSlot;
        ulong key = LumonSceneVirtualPageKeyUtil.Pack(slot, work.VirtualPageIndex);
        Span<uint> voxels = stackalloc uint[16];
        if (scene == null || slot >= slotOwners.Length || slot >= slotGenerations.Length ||
            !virtualToPhysical.TryGetValue(key, out uint page) || page != work.PhysicalPageId ||
            !scene.TryReadCaptureIdentity(slotOwners[slot], work.PatchId, voxels)) return false;
        captureIdentities[page] = new(key, slotGenerations[slot], work.PatchId, ++nextCaptureRevision, ImmutableArray.Create(voxels));
        unavailableCaptures.Remove(page);
        return true;
    }

    /// <summary>Compares only published source inputs; unavailable pages retain their data for a later comparison.</summary>
    internal void SynchronizeCaptureIdentities(TraceGeometryGpuScene scene)
    {
        if (checkedGeometryRevision == scene.Revision) return;
        checkedGeometryRevision = scene.Revision;
        Span<uint> voxels = stackalloc uint[16];
        foreach (var pair in captureIdentities.ToArray())
        {
            var identity = pair.Value;
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(identity.Key);
            if (!virtualToPhysical.TryGetValue(identity.Key, out uint page) || page != pair.Key ||
                slot >= slotGenerations.Length || slotGenerations[slot] != identity.Generation)
            {
                captureIdentities.Remove(pair.Key); unavailableCaptures.Remove(pair.Key);
                continue;
            }
            if (!scene.TryReadCaptureIdentity(slotOwners[slot], identity.PatchId, voxels))
            {
                unavailableCaptures.Add(pair.Key);
                continue;
            }
            unavailableCaptures.Remove(pair.Key);
            if (voxels.SequenceEqual(identity.Voxels.AsSpan())) continue;

            // Invalidate only the page whose actual capture inputs changed. The existing retry
            // sweep will recapture it without restarting unrelated captures or lighting work.
            captureIdentities.Remove(pair.Key);
            int virtualPage = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(identity.Key);
            int index = checked((int)((long)slot << 14) + virtualPage);
            var old = pageTableMirror[index];
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(old) |
                LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.NeedsRelight;
            var updated = LumonScenePageTableEntryPacking.Pack(pair.Key, flags);
            pageTableMirror[index] = updated;
            pageTableStats.ApplyEntryChange(slot, in old, in updated);
            UploadPageTableEntryMip0((int)slot, virtualPage, updated.Packed);
            QueueCaptureRetry(pair.Key, identity.Key);
        }
    }
    #endregion
}
