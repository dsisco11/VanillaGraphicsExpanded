using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnProbeSh9GatherShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_probe_sh9_gather program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_probe_sh9_gather", "lumon_probe_sh9_gather.vsh", "lumon_probe_sh9_gather.fsh", 4, [Visibility, World, WorldGather], LumOnShaderConstants.World(true));
    #endregion
}
