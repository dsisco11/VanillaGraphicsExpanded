using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/GpuUniformRingBufferIntegrationTests_2", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/GpuUniformRingBufferIntegrationTests_2.csh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal static partial class UniformRingBufferReadbackComputeShader
{
}
