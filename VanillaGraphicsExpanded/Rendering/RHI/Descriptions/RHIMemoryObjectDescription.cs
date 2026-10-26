namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Describes legal dedicated-memory policy and optional explicit import.</summary>
public readonly record struct RHIMemoryObjectDescription(bool? Dedicated = null, RHIExternalHandle? Import = null, ulong Size = 0);
