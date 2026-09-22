using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/PbrMaterialParamsTextureSmokeTests_2", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/PbrMaterialParamsTextureSmokeTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/PbrMaterialParamsTextureSmokeTests_2.fsh")]
internal static partial class PbrMaterialParamsShaderProgram
{
}
