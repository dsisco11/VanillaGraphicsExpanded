using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnHzbCopyShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_hzb_copy program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_hzb_copy", "lumon_hzb_copy.vsh", "lumon_hzb_copy.fsh", 1);
    #endregion
}
