using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Invalidates surface-cache captures and irradiance after shared geometry changes.</summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer
{
    private TraceGeometryGpuScene? historyGeometry;
    private long historyInvalidation = -1;
    private int historyAtlas;
    private GpuComputePipeline? resetIrradiance;

    /// <summary>Identifies the current capture and lighting history for partial relight schedules.</summary>
    internal long GeometryHistoryRevision { get; private set; }

    #region Shared geometry invalidation
    /// <summary>Resets history before sampling and requeues resident pages when previous tracing inputs became stale.</summary>
    private bool EnsureGeometryHistoryCurrent()
    {
        var atlas = physicalPools.Near.GpuResources?.IrradianceAtlas;
        if (!configured || atlas == null) return false;
        var scene = occupancyClipmap?.PrepareScene();
        long revision = scene?.InvalidationRevision ?? -1;
        if (ReferenceEquals(historyGeometry, scene) && historyInvalidation == revision && historyAtlas == atlas.TextureId) return true;
        if (resetIrradiance == null && !GpuComputePipeline.TryCreateFromAssets(capi, "lumonscene_reset_irradiance",
            out resetIrradiance, out _, out var log, preferSpirv: true))
        { capi.Logger.Warning("[VGE] Cannot invalidate surface lighting: {0}", log); return false; }
        using (resetIrradiance!.UseScope())
        {
            atlas.BindImageUnit(0, TextureAccess.WriteOnly, layered: true, format: SizedInternalFormat.Rgba16f);
            GL.DispatchCompute((atlas.Width + 7) / 8, (atlas.Height + 7) / 8, atlas.Depth);
        }
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
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
        historyGeometry = scene; historyInvalidation = revision; historyAtlas = atlas.TextureId;
        GeometryHistoryRevision++;
        return true;
    }

    /// <summary>Releases invalidation resources and forgets the prior world generation.</summary>
    private void ReleaseGeometryHistory()
    {
        resetIrradiance?.Dispose(); resetIrradiance = null;
        historyGeometry = null; historyInvalidation = -1; historyAtlas = 0;
    }
    #endregion
}
