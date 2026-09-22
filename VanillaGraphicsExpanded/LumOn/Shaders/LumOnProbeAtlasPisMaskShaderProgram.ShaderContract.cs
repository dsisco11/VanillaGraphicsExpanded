using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnProbeAtlasPisMaskShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_probe_atlas_pis_mask program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_probe_atlas_pis_mask", "lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_pis_mask.fsh", 8, [Pis, PisMask], LumOnShaderConstants.Mask());
    #endregion
}
