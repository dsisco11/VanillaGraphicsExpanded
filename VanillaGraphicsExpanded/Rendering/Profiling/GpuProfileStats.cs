namespace VanillaGraphicsExpanded.Rendering.Profiling;

/// <summary>Describes collected debug GPU timing samples.</summary>
public readonly record struct GpuProfileStats(
    float LastMs,
    float AvgMs,
    float MinMs,
    float MaxMs,
    int SampleCount,
    int Width,
    int Height);
