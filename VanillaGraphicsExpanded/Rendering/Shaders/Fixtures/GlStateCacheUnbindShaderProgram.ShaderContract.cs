using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class GlStateCacheUnbindShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/GlStateCacheUnbindIntegrationTests_2 program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("tests/GlStateCacheUnbindIntegrationTests_2", "tests/GlStateCacheUnbindIntegrationTests_1.vsh", "tests/GlStateCacheUnbindIntegrationTests_2.fsh", 1);
    #endregion
}
