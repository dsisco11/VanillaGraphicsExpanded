namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Invalidates surface-cache captures and irradiance after shared geometry changes.</summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer
{
    private LumonSceneIrradianceHistory? irradianceHistory;

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
        GeometryHistoryRevision++;
        return true;
    }

    /// <summary>Releases invalidation resources and forgets the prior world generation.</summary>
    private void ReleaseGeometryHistory()
    {
        irradianceHistory?.Dispose(); irradianceHistory = null;
    }
    #endregion
}
