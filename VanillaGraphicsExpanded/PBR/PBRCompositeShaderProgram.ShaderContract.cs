using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class PBRCompositeShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the pbr_composite program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("pbr_composite", "pbr_composite.vsh", "pbr_composite.fsh", 8, [Lighting, Composite]);
    #endregion
}
