using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Declares the GPU binding contract for VgeDebugLinesShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IVgeDebugLinesShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgeDebugLinesParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeDebugLinesParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer Parameters { set; }
    #endregion
}
