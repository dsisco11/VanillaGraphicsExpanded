using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the offline array and independent-unit namespace validation executable.</summary>
[ShaderProgram("Contract", "tests/prepared_binding", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/prepared_binding.csh")]
[ShaderBindingSet(typeof(IPreparedBindingComputeBindings))]
internal static partial class PreparedBindingComputeShader { }
