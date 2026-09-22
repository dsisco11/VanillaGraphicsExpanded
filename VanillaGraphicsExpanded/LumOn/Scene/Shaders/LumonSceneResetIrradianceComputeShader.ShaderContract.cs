using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class LumonSceneResetIrradianceComputeShader
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumonscene_reset_irradiance program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Compute("lumonscene_reset_irradiance", "lumonscene_reset_irradiance.csh");
    #endregion
}
