using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Declares the GPU binding contract for VgeWorldProbeOrbsPointsShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IVgeWorldProbeOrbsPointsShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer LumOnFrame { set; }
    /// <summary>Declares the LumOnWorldProbeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer LumOnWorldProbe { set; }
    /// <summary>Declares the VgeWorldProbeOrbsPointsParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeWorldProbeOrbsPointsParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer Parameters { set; }
    #endregion
}
