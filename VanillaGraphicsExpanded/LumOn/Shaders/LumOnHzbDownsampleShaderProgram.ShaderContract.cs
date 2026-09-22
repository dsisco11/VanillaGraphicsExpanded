using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnHzbDownsampleShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_hzb_downsample program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_hzb_downsample", "lumon_hzb_downsample.vsh", "lumon_hzb_downsample.fsh", 1);
    #endregion
}
