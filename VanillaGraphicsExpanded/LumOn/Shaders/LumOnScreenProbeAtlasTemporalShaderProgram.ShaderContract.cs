using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnScreenProbeAtlasTemporalShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_probe_atlas_temporal program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_probe_atlas_temporal", "lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh", 4, [Pis, AtlasUpdate], [new(1, LumOnShaderOptions.AtlasTexelsPerFrame)]);
    #endregion
}
