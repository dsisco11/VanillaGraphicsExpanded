using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Owns local-ray companion resources and publication independently of surface-cache capture.</summary>
internal sealed partial class LumonSceneOccupancyClipmapUpdateRenderer
{
    private LocalTraceGpuScene? localTraceScene;
    private readonly LocalTraceMaterialRegistry localMaterials = new();
    private VectorInt3? scheduledLocalOrigin;

    #region Local Scene Lifecycle
    /// <summary>Maintains a region-aligned consumer window over the existing snapshot stream.</summary>
    private void EnsureLocalTraceScene(int x, int y, int z)
    {
        if (resources is null || chunkVersions is null) return;
        localTraceScene ??= new LocalTraceGpuScene(resources.Resolution);
        localTraceScene.Prepare(new VectorInt3(x, y, z), chunkVersions);
        var origin = localTraceScene.Origin;
        if (scheduledLocalOrigin != origin)
        {
            // Region alignment can expose local cells before the coarser window moves.
            int extent = localTraceScene.Resolution;
            for (int rz = origin.Z; rz < origin.Z + extent; rz += 32)
            for (int ry = origin.Y; ry < origin.Y + extent; ry += 32)
            for (int rx = origin.X; rx < origin.X + extent; rx += 32)
            {
                if (scheduledLocalOrigin is { } old &&
                    rx >= old.X && rx < old.X + extent &&
                    ry >= old.Y && ry < old.Y + extent &&
                    rz >= old.Z && rz < old.Z + extent) continue;
                EnqueueRegion(rx >> 5, ry >> 5, rz >> 5, "LocalWindowEnter");
            }
            scheduledLocalOrigin = origin;
        }
    }

    /// <summary>Rejects dirty cached regions before the earlier Opaque stage consumes last frame's data.</summary>
    public LocalTraceGpuScene? PrepareLocalTraceScene()
    {
        if (!config.LumOn.Enabled || localTraceScene is null || chunkVersions is null) return null;
        if (!TryGetCameraPosBlock(out int x, out int y, out int z)) return null;
        localTraceScene.Prepare(new VectorInt3(x, y, z), chunkVersions);
        return localTraceScene;
    }

    /// <summary>Disposes companion resources with their owning occupancy scene.</summary>
    private void DisposeLocalTraceScene()
    {
        localTraceScene?.Dispose();
        localTraceScene = null;
        scheduledLocalOrigin = null;
        localMaterials.Reset();
    }
    #endregion
}
