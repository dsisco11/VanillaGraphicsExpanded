using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class PBRDirectLightingShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the pbr_direct_lighting program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("pbr_direct_lighting", "pbr_direct_lighting.vsh", "pbr_direct_lighting.fsh", 1);
    #endregion
}
