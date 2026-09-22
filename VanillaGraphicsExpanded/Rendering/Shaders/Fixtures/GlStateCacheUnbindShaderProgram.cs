using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/GlStateCacheUnbindIntegrationTests_2", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/GlStateCacheUnbindIntegrationTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/GlStateCacheUnbindIntegrationTests_2.fsh")]
internal static partial class GlStateCacheUnbindShaderProgram
{
}
