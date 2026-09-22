using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class UniformRingBufferReadbackComputeShader
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/GpuUniformRingBufferIntegrationTests_2 program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Compute("tests/GpuUniformRingBufferIntegrationTests_2", "tests/GpuUniformRingBufferIntegrationTests_2.csh");
    #endregion
}
