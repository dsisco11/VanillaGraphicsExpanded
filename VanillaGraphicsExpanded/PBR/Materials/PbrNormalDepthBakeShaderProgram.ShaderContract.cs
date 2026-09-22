using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class PbrNormalDepthBakeShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the pbr_normaldepth_bake program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("pbr_normaldepth_bake", "pbr_normaldepth_bake.vsh", "pbr_normaldepth_bake.fsh", 1);
    #endregion
}
