using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Tracks the scene dependencies that invalidate retained screen-probe lighting.</summary>
internal sealed class ProbeLightingHistoryDependencies
{
    private TraceGeometryGpuScene? geometry;
    private long geometryRevision = -1;
    private long lightingRevision = -1;
    private PBR.LightingCalibration? calibration;

    #region Public API
    /// <summary>Rejects changed geometry or lighting dependencies while preserving ordinary bounce generations.</summary>
    public bool Synchronize(TraceGeometryGpuScene? scene, SurfaceLightingSnapshot? lighting,
        PBR.LightingCalibration? sourceCalibration = null)
    {
        // Generation advances for progressive bounces; only dependency changes invalidate their consumers.
        long nextGeometry = scene?.Revision ?? -1;
        long nextLighting = lighting?.DependencyRevision ?? -1;
        var nextCalibration = sourceCalibration ?? Rendering.VgeFrameRenderer.CurrentLightingCalibration;
        // Calibration changes also invalidate sky-only probes when no surface cache is available.
        bool changed = !ReferenceEquals(scene, geometry) || nextGeometry != geometryRevision
            || nextLighting != lightingRevision || calibration != nextCalibration;
        geometry = scene;
        geometryRevision = nextGeometry;
        lightingRevision = nextLighting;
        calibration = nextCalibration;
        return changed;
    }
    #endregion
}
