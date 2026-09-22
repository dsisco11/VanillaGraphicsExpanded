using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the isolated build validator's Graphics program.</summary>
[ShaderProgram("Contract", "fixture", 1, Scope = "build-validation")]
[ShaderStage("Contract", ShaderStageKind.Vertex, "fixture.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "fixture.fsh")]
internal static partial class BuildValidationGraphicsShader { }
