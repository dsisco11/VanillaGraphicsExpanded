using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Declares the GPU binding contract for RasterDepthReductionShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IRasterDepthReductionShaderBindings
{
    #region Public API
    /// <summary>Declares the VgeDepthHierarchyParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeDepthHierarchyParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the hzbDepth Sampler slot.</summary>
    [ShaderBinding("hzbDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? HzbDepth { set; }
    #endregion
}
