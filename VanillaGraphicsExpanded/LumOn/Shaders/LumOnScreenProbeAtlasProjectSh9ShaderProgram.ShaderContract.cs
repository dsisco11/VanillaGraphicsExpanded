using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnScreenProbeAtlasProjectSh9ShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_probe_atlas_project_sh9 program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_probe_atlas_project_sh9", "lumon_probe_atlas_project_sh9.vsh", "lumon_probe_atlas_project_sh9.fsh", 1);
    #endregion
}
