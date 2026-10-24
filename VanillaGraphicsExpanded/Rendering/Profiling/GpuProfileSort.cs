namespace VanillaGraphicsExpanded.Rendering.Profiling;

/// <summary>Selects the ordering of collected GPU timing samples.</summary>
public enum GpuProfileSort
{
    Name = 0,
    LastMs = 1,
    AvgMs = 2,
    MaxMs = 3
}
