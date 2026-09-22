using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the isolated build validator's Compute program.</summary>
[ShaderProgram("Contract", "fixture_compute", 1, Scope = "build-validation")]
[ShaderStage("Contract", ShaderStageKind.Compute, "fixture.csh")]
internal static partial class BuildValidationComputeShader { }
