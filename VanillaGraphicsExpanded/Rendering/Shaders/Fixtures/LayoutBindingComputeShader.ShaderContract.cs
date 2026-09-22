using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class LayoutBindingComputeShader
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/GpuProgramLayoutBindingTests_1 program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Compute("tests/GpuProgramLayoutBindingTests_1", "tests/GpuProgramLayoutBindingTests_1.csh");
    #endregion
}
