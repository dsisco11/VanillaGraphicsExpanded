using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "pbr_normaldepth_bake", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_normaldepth_bake.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_normaldepth_bake.fsh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal static partial class PbrNormalDepthBakeShaderProgram
{

    #region Private: GPU binding declarations
    /// <summary>Declares the VgePbrNormalDepthBakeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgePbrNormalDepthBakeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private static partial ShaderUniformBlockBinding Parameters { get; }
    #endregion

}
