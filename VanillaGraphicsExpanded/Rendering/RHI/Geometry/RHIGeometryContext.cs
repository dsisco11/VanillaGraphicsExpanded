namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Captures query-free originating namespace and completed configuration knowledge for external certification.</summary>
internal readonly record struct RHIGeometryContext((nint Handle, long Generation) Origin, ulong ConfigurationEpoch);
