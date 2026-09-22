using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class VgeWorldProbeOrbsPointsShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the vge_worldprobe_orbs_points program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("vge_worldprobe_orbs_points", "vge_worldprobe_orbs_points.vsh", "vge_worldprobe_orbs_points.fsh", 2, [Visibility, Orbs], [new(13, LumOnShaderOptions.WorldProbeOctahedralSize), new(14, LumOnShaderOptions.WorldProbeResolution)]);
    #endregion
}
