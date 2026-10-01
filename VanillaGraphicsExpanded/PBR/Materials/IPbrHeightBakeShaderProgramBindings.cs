using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Declares the GPU binding contract for PbrHeightBakeShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IPbrHeightBakeShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgePbrHeightBakeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgePbrHeightBakeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer Parameters { set; }
    #endregion
}
