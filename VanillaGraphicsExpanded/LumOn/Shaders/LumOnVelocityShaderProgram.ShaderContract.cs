using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnVelocityShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_velocity program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_velocity", "lumon_velocity.vsh", "lumon_velocity.fsh", 1);
    #endregion
}
