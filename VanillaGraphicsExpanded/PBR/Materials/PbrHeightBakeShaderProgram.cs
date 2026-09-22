using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Specialized shader program for PBR height-bake passes.
/// Automatically registers the shared VgePbrHeightBakeParamsUBO binding.
/// </summary>
[ShaderProgram("CombineContract", "pbr_heightbake_combine", 1)]
[ShaderStage("CombineContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("CombineContract", ShaderStageKind.Fragment, "pbr_heightbake_combine.fsh")]
[ShaderProgram("CopyContract", "pbr_heightbake_copy", 1)]
[ShaderStage("CopyContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("CopyContract", ShaderStageKind.Fragment, "pbr_heightbake_copy.fsh")]
[ShaderProgram("DivergenceContract", "pbr_heightbake_divergence", 1)]
[ShaderStage("DivergenceContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("DivergenceContract", ShaderStageKind.Fragment, "pbr_heightbake_divergence.fsh")]
[ShaderProgram("Gauss1dContract", "pbr_heightbake_gauss1d", 1)]
[ShaderStage("Gauss1dContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("Gauss1dContract", ShaderStageKind.Fragment, "pbr_heightbake_gauss1d.fsh")]
[ShaderProgram("GradientContract", "pbr_heightbake_gradient", 1)]
[ShaderStage("GradientContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("GradientContract", ShaderStageKind.Fragment, "pbr_heightbake_gradient.fsh")]
[ShaderProgram("JacobiContract", "pbr_heightbake_jacobi", 1)]
[ShaderStage("JacobiContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("JacobiContract", ShaderStageKind.Fragment, "pbr_heightbake_jacobi.fsh")]
[ShaderProgram("LuminanceContract", "pbr_heightbake_luminance", 1)]
[ShaderStage("LuminanceContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("LuminanceContract", ShaderStageKind.Fragment, "pbr_heightbake_luminance.fsh")]
[ShaderProgram("NormalizeContract", "pbr_heightbake_normalize", 1)]
[ShaderStage("NormalizeContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("NormalizeContract", ShaderStageKind.Fragment, "pbr_heightbake_normalize.fsh")]
[ShaderProgram("PackToAtlasContract", "pbr_heightbake_pack_to_atlas", 1)]
[ShaderStage("PackToAtlasContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("PackToAtlasContract", ShaderStageKind.Fragment, "pbr_heightbake_pack_to_atlas.fsh")]
[ShaderProgram("ProlongateAddContract", "pbr_heightbake_prolongate_add", 1)]
[ShaderStage("ProlongateAddContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("ProlongateAddContract", ShaderStageKind.Fragment, "pbr_heightbake_prolongate_add.fsh")]
[ShaderProgram("ResidualContract", "pbr_heightbake_residual", 1)]
[ShaderStage("ResidualContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("ResidualContract", ShaderStageKind.Fragment, "pbr_heightbake_residual.fsh")]
[ShaderProgram("RestrictContract", "pbr_heightbake_restrict", 1)]
[ShaderStage("RestrictContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("RestrictContract", ShaderStageKind.Fragment, "pbr_heightbake_restrict.fsh")]
[ShaderProgram("SubContract", "pbr_heightbake_sub", 1)]
[ShaderStage("SubContract", ShaderStageKind.Vertex, "pbr_heightbake_fullscreen.vsh")]
[ShaderStage("SubContract", ShaderStageKind.Fragment, "pbr_heightbake_sub.fsh")]
internal sealed partial class PbrHeightBakeShaderProgram : GpuProgram
{
    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => System.Linq.Enumerable.Single(Contracts, contract => contract.Identity == PassName);

    private const string ParamsBlockName = "VgePbrHeightBakeParamsUBO";

    /// <summary>Uses the registered height-bake stage pair and binding declarations.</summary>
    public PbrHeightBakeShaderProgram(string passName, string domain)
    {
        PassName = passName;
        AssetDomain = domain;
        ProgramLayout.RegisterContract(ProgramContract.Stages[1].Bindings);

    }
}
