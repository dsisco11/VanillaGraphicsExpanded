using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/GpuUniformRingBufferIntegrationTests_1", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/GpuUniformRingBufferIntegrationTests_1.csh")]
internal static partial class UniformRingBufferComputeShader
{
}
