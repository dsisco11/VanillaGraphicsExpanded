using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnScreenProbeAtlasProjectSHShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_probe_atlas_project_sh program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_probe_atlas_project_sh", "lumon_probe_atlas_project_sh.vsh", "lumon_probe_atlas_project_sh.fsh", 1);
    #endregion
}
