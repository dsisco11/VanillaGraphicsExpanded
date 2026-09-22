using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/pbr_direct_fullscreen", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/fullscreen_uv.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_direct_lighting.fsh")]
internal static partial class PbrDirectFullscreenShaderProgram
{
}
