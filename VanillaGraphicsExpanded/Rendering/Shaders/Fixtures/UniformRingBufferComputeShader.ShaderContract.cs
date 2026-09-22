using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class UniformRingBufferComputeShader
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/GpuUniformRingBufferIntegrationTests_1 program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Compute("tests/GpuUniformRingBufferIntegrationTests_1", "tests/GpuUniformRingBufferIntegrationTests_1.csh");
    #endregion
}
