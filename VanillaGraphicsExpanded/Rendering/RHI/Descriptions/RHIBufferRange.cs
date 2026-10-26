namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects an explicit native-width buffer interval, never an implicit whole-buffer sentinel.</summary>
public readonly record struct RHIBufferRange(GpuBufferRef Buffer, nint Offset, nint Length);
