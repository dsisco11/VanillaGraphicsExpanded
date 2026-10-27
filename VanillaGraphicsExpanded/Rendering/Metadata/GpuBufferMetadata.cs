using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes actual buffer storage; intended binding roles and content extent remain wrapper policy.</summary>
public readonly record struct GpuBufferMetadata(nint Capacity, BufferUsage Usage,
    bool ImmutableStorage = false, StorageAccess StorageFlags = 0);
