namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Debug counters for LumOn performance monitoring and debugging.
/// Track these metrics for profiling and debugging.
/// </summary>
public class LumOnDebugCounters
{
    /// <summary>Total probes in grid (probeW × probeH)</summary>
    public int TotalProbes { get; set; }

    /// <summary>Probes marked valid this frame</summary>
    public int ValidProbes { get; set; }

    /// <summary>Probes marked as edge (partial validity)</summary>
    public int EdgeProbes { get; set; }

    /// <summary>Total rays traced this frame (validProbes × raysPerProbe)</summary>
    public int RaysTraced { get; set; }

    /// <summary>Rays that hit geometry</summary>
    public int RayHits { get; set; }

    /// <summary>Rays that missed (sky fallback)</summary>
    public int RayMisses { get; set; }

    /// <summary>Hit rate percentage (hits / traced)</summary>
    public float HitRate => RaysTraced > 0 ? (float)RayHits / RaysTraced * 100f : 0f;

    /// <summary>Probes with valid temporal history</summary>
    public int TemporalValidProbes { get; set; }

    /// <summary>Probes rejected (disoccluded)</summary>
    public int TemporalRejectedProbes { get; set; }

    /// <summary>Time spent in probe anchor pass (ms)</summary>
    public float ProbeAnchorPassMs { get; set; }

    /// <summary>Time spent in probe trace pass (ms)</summary>
    public float ProbeTracePassMs { get; set; }

    /// <summary>Time spent in temporal pass (ms)</summary>
    public float TemporalPassMs { get; set; }

    /// <summary>Time spent in gather pass (ms)</summary>
    public float GatherPassMs { get; set; }

    /// <summary>Time spent in upsample pass (ms)</summary>
    public float UpsamplePassMs { get; set; }

    /// <summary>Total frame time for LumOn passes (ms)</summary>
    public float TotalFrameMs { get; set; }

    /// <summary>
    /// Reset all counters for a new frame.
    /// </summary>
    public void Reset()
    {
        ValidProbes = 0;
        EdgeProbes = 0;
        RaysTraced = 0;
        RayHits = 0;
        RayMisses = 0;
        TemporalValidProbes = 0;
        TemporalRejectedProbes = 0;
        ProbeAnchorPassMs = 0;
        ProbeTracePassMs = 0;
        TemporalPassMs = 0;
        GatherPassMs = 0;
        UpsamplePassMs = 0;
        TotalFrameMs = 0;
    }

    /// <summary>
    /// Get formatted debug string for overlay display.
    /// </summary>
    public string[] GetDebugLines()
    {
        return
        [
            $"LumOn Probes: {ValidProbes}/{TotalProbes} valid, {EdgeProbes} edge",
            $"Rays: {RaysTraced} traced, {HitRate:F1}% hit rate",
            $"Temporal: {TemporalValidProbes} valid, {TemporalRejectedProbes} rejected",
            $"Time: {TotalFrameMs:F2}ms (A:{ProbeAnchorPassMs:F2} T:{ProbeTracePassMs:F2} " +
            $"Tp:{TemporalPassMs:F2} G:{GatherPassMs:F2} U:{UpsamplePassMs:F2})"
        ];
    }
}
