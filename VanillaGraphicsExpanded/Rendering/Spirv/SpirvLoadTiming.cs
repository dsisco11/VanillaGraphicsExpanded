namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Separate binary-read/validation and driver binary-load/specialization durations.</summary>
internal sealed record SpirvLoadTiming(string Source, double ReadMilliseconds, double BinaryLoadMilliseconds, double SpecializeMilliseconds);
