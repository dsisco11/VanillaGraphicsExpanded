using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class GlobalDefinesShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/vge_global_defines_smoke program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("tests/vge_global_defines_smoke", "tests/vge_global_defines_smoke.vsh", "tests/vge_global_defines_smoke.fsh", 16, [Lighting, Composite, Ao]);
    #endregion
}
