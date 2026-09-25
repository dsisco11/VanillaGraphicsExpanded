using System.Threading;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Separates incompatible scene history from per-page capture identity changes.</summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer
{
    private LumonSceneIrradianceHistory? irradianceHistory;

    /// <summary>Identifies the current capture and lighting history for partial relight schedules.</summary>
    internal long GeometryHistoryRevision { get; private set; }

    #region Shared geometry invalidation
    /// <summary>Checks per-page identities, resetting all captures only when their scene or atlas lifetime is incompatible.</summary>
    private bool EnsureGeometryHistoryCurrent()
    {
        var atlas = physicalPools.Near.GpuResources?.IrradianceAtlas;
        if (!configured || atlas == null) return false;
        var scene = traceGeometry?.PrepareScene();
        irradianceHistory ??= new(capi);
        if (!irradianceHistory.TrySynchronize(scene, atlas, out bool invalidated)) return false;
        if (!invalidated)
        {
            SynchronizeCaptureIdentities(scene!);
            return true;
        }
        captureIdentities.Clear(); unavailableCaptures.Clear(); checkedGeometryRevision = -1;
        ClearCaptureAdmission();
        // Only incompatible scene/material identities or replacement atlas storage require a full recapture.
        foreach (var pair in virtualToPhysical)
        {
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Key);
            int page = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(pair.Key);
            int index = checked((int)slot * VirtualPagesPerChunk + page);
            var old = pageTableMirror[index];
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(old) |
                LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.NeedsRelight;
            var updated = LumonScenePageTableEntryPacking.Pack(pair.Value, flags);
            pageTableMirror[index] = updated; pageTableStats.ApplyEntryChange(slot, in old, in updated);
            UploadPageTableEntryMip0((int)slot, page, updated.Packed);
        }
        ResetRecaptureList();
        Interlocked.Exchange(ref recaptureAllRequested, 1);
        GeometryHistoryRevision++;
        return true;
    }

    /// <summary>Releases invalidation resources and forgets the prior world generation.</summary>
    private void ReleaseGeometryHistory()
    {
        ClearCaptureAdmission();
        irradianceHistory?.Dispose(); irradianceHistory = null;
        captureIdentities.Clear(); unavailableCaptures.Clear(); checkedGeometryRevision = -1;
    }
    #endregion
}
