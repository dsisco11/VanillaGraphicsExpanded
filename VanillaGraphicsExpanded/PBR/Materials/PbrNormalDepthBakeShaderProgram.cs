using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "pbr_normaldepth_bake", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_normaldepth_bake.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_normaldepth_bake.fsh")]
internal static partial class PbrNormalDepthBakeShaderProgram
{
}
