using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable declaration for tests/render_infrastructure.</summary>
[ShaderProgram("Contract", "tests/render_infrastructure", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/render_infrastructure.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/render_infrastructure.fsh")]
internal static partial class RenderInfrastructureShaderProgram { }
