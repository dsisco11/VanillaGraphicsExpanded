using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class RenderInfrastructureShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/render_infrastructure program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("tests/render_infrastructure", "tests/render_infrastructure.vsh", "tests/render_infrastructure.fsh", 1);
    #endregion
}
