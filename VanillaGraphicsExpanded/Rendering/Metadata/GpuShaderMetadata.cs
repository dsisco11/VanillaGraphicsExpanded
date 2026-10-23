using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Records the shader object's fixed native type independently of its authored binding contract.</summary>
public readonly record struct GpuShaderMetadata(ShaderType Type);
