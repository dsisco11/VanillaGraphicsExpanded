using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Invalidates surface-cache captures and irradiance after shared geometry changes.</summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer
{
    private LumonSceneIrradianceHistory? irradianceHistory;
    private readonly HashSet<ulong> captureRetries = new();

    /// <summary>Identifies the current capture and lighting history for partial relight schedules.</summary>
    internal long GeometryHistoryRevision { get; private set; }

    #region Shared geometry invalidation
    /// <summary>Resets history before sampling and requeues resident pages when previous tracing inputs became stale.</summary>
    private bool EnsureGeometryHistoryCurrent()
    {
        var atlas = physicalPools.Near.GpuResources?.IrradianceAtlas;
        if (!configured || atlas == null) return false;
        var scene = traceGeometry?.PrepareScene();
        irradianceHistory ??= new(capi);
        if (!irradianceHistory.TrySynchronize(scene, atlas, out bool invalidated)) return false;
        if (!invalidated) return true;
        // Conservative invalidation covers indirect ray dependencies beyond the page's own chunk.
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

    /// <summary>Resumes failed captures after the current bounded sweep, without restarting or starving later pages.</summary>
    private void ResumeCaptureRetries()
    {
        if (recaptureVirtualPageKeys != null || captureRetries.Count == 0) return;
        recaptureVirtualPageKeys = ArrayPool<ulong>.Shared.Rent(captureRetries.Count);
        recaptureCount = 0;
        foreach (ulong key in captureRetries)
        {
            // Residency may have changed while this retry waited. Only current unresolved pages need work.
            if (!virtualToPhysical.ContainsKey(key)) continue;
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
            int page = (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
            int index = checked((int)slot * VirtualPagesPerChunk + page);
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(pageTableMirror[index]);
            if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsCapture) != 0)
                recaptureVirtualPageKeys[recaptureCount++] = key;
        }
        captureRetries.Clear();
        recaptureCursor = 0;
        if (recaptureCount == 0) ResetRecaptureList();
    }

    /// <summary>Releases invalidation resources and forgets the prior world generation.</summary>
    private void ReleaseGeometryHistory()
    {
        captureRetries.Clear();
        irradianceHistory?.Dispose(); irradianceHistory = null;
    }
    #endregion
}
