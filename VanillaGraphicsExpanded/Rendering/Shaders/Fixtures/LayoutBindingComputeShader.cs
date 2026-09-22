using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable declaration for tests/GpuProgramLayoutBindingTests_1.</summary>
[ShaderProgram("Contract", "tests/GpuProgramLayoutBindingTests_1", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/GpuProgramLayoutBindingTests_1.csh")]
internal static partial class LayoutBindingComputeShader { }
