using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Declares the GPU binding contract for LiquidDepthShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILiquidDepthShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgeLiquidDepthFrameParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidDepthFrameParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer FrameParameters { set; }
    /// <summary>Declares the VgeLiquidDrawParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidDrawParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer DrawParameters { set; }
    /// <summary>Declares the VgeLiquidWaveParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidWaveParams", ShaderBindingKind.UniformBlock, 15, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer WaveParameters { set; }
    #endregion
}
