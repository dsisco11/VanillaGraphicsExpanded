namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains the exact initialized range of a borrowed storage-capable buffer.</summary>
internal readonly record struct GpuStorageBufferBinding(GpuBufferObject? Buffer, int OffsetBytes, int SizeBytes);
