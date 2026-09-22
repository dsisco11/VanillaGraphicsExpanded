using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/framebuffer_blend", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/framebuffer_blend.fsh")]
internal static partial class FramebufferBlendShaderProgram
{
}
