using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class PbrMaterialParamsShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/PbrMaterialParamsTextureSmokeTests_2 program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("tests/PbrMaterialParamsTextureSmokeTests_2", "tests/PbrMaterialParamsTextureSmokeTests_1.vsh", "tests/PbrMaterialParamsTextureSmokeTests_2.fsh", 1);
    #endregion
}
