namespace VanillaGraphicsExpanded.Rendering.Profiling;

/// <summary>Pairs a named GPU event with its available debug measurements.</summary>
public readonly record struct GpuProfileEntry(string Name, GpuProfileStats Stats);
