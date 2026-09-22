using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnUpsampleShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_upsample program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_upsample", "lumon_upsample.vsh", "lumon_upsample.fsh", 4, [Upsample]);
    #endregion
}
