using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class PbrDirectFullscreenShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/pbr_direct_fullscreen program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Pair("tests/pbr_direct_fullscreen", "tests/fullscreen_uv.vsh", global::VanillaGraphicsExpanded.PBR.PBRDirectLightingShaderProgram.Contract);
    #endregion
}
