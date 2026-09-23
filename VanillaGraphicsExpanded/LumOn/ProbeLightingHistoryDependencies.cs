using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Tracks the scene dependencies that invalidate retained screen-probe lighting.</summary>
internal sealed class ProbeLightingHistoryDependencies
{
    private TraceGeometryGpuScene? geometry;
    private long geometryRevision = -1;
    private long lightingRevision = -1;

    #region Synchronization
    /// <summary>Rejects changed geometry or lighting dependencies while preserving ordinary bounce generations.</summary>
    public bool Synchronize(TraceGeometryGpuScene? scene, SurfaceLightingSnapshot? lighting)
    {
        // Generation advances for progressive bounces; only dependency changes invalidate their consumers.
        long nextGeometry = scene?.Revision ?? -1;
        long nextLighting = lighting?.DependencyRevision ?? -1;
        bool changed = !ReferenceEquals(scene, geometry) || nextGeometry != geometryRevision || nextLighting != lightingRevision;
        geometry = scene;
        geometryRevision = nextGeometry;
        lightingRevision = nextLighting;
        return changed;
    }
    #endregion
}
