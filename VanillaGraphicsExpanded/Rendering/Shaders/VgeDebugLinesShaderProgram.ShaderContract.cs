using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class VgeDebugLinesShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the vge_debug_lines program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("vge_debug_lines", "vge_debug_lines.vsh", "vge_debug_lines.fsh", 1);
    #endregion
}
