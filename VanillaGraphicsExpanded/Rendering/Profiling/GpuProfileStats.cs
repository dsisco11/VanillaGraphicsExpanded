namespace VanillaGraphicsExpanded.Rendering.Profiling;

public readonly record struct GpuProfileStats(
    float LastMs,
    float AvgMs,
    float MinMs,
    float MaxMs,
    int SampleCount,
    int Width,
    int Height);
