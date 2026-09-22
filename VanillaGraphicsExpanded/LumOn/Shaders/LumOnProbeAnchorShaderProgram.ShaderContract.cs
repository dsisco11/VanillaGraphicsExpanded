using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnProbeAnchorShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_probe_anchor program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_probe_anchor", "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", 1);
    #endregion
}
