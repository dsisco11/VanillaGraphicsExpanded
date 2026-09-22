using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal partial class PbrHeightBakeShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the pbr_heightbake_combine program.</summary>
    internal static GpuShaderContract CombineContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_combine", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_combine.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_copy program.</summary>
    internal static GpuShaderContract CopyContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_copy", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_copy.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_divergence program.</summary>
    internal static GpuShaderContract DivergenceContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_divergence", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_divergence.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_gauss1d program.</summary>
    internal static GpuShaderContract Gauss1dContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_gauss1d", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_gauss1d.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_gradient program.</summary>
    internal static GpuShaderContract GradientContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_gradient", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_gradient.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_jacobi program.</summary>
    internal static GpuShaderContract JacobiContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_jacobi", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_jacobi.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_luminance program.</summary>
    internal static GpuShaderContract LuminanceContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_luminance", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_luminance.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_normalize program.</summary>
    internal static GpuShaderContract NormalizeContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_normalize", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_normalize.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_pack_to_atlas program.</summary>
    internal static GpuShaderContract PackToAtlasContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_pack_to_atlas", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_pack_to_atlas.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_prolongate_add program.</summary>
    internal static GpuShaderContract ProlongateAddContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_prolongate_add", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_prolongate_add.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_residual program.</summary>
    internal static GpuShaderContract ResidualContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_residual", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_residual.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_restrict program.</summary>
    internal static GpuShaderContract RestrictContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_restrict", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_restrict.fsh", 1);
    /// <summary>Immutable declaration for the pbr_heightbake_sub program.</summary>
    internal static GpuShaderContract SubContract { get; } = ShaderProgramDeclaration.Graphics("pbr_heightbake_sub", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_sub.fsh", 1);

    /// <summary>All program declarations owned by this shader family.</summary>
    internal static System.Collections.Generic.IReadOnlyList<GpuShaderContract> Contracts { get; } = System.Array.AsReadOnly<GpuShaderContract>([CombineContract, CopyContract, DivergenceContract, Gauss1dContract, GradientContract, JacobiContract, LuminanceContract, NormalizeContract, PackToAtlasContract, ProlongateAddContract, ResidualContract, RestrictContract, SubContract]);
    #endregion
}
