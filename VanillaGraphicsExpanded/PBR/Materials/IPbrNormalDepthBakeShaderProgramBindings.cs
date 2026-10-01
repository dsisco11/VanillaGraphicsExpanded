using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Declares the GPU binding contract for PbrNormalDepthBakeShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IPbrNormalDepthBakeShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgePbrNormalDepthBakeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgePbrNormalDepthBakeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    ShaderUniformBlockBinding Parameters { get; }
    #endregion
}
