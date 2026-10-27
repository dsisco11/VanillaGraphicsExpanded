using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Records the shader object's fixed native type independently of its authored binding contract.</summary>
public readonly record struct GpuShaderMetadata(ShaderStage Type);
