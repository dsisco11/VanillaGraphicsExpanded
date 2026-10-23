namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Identifies a native resource by kind and native-width name within its manager's context.</summary>
public readonly record struct GpuResourceHandle(GpuResourceKind Kind, nint Id);
