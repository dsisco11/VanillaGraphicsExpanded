using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes actual buffer storage; intended binding roles and content extent remain wrapper policy.</summary>
public readonly record struct GpuBufferMetadata(nint Capacity, BufferUsageHint Usage);
