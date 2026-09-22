using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnCombineShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_combine program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_combine", "lumon_combine.vsh", "lumon_combine.fsh", 16, [Lighting, Composite, Ao]);
    #endregion
}
