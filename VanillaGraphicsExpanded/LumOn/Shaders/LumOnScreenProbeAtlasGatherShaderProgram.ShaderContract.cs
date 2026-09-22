using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnScreenProbeAtlasGatherShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_probe_atlas_gather program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_probe_atlas_gather", "lumon_probe_atlas_gather.vsh", "lumon_probe_atlas_gather.fsh", 4, [Visibility, World, WorldGather], LumOnShaderConstants.World(true));
    #endregion
}
