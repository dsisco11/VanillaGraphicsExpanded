using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Declares the GPU binding contract for VgeWorldProbeOrbsPointsShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IVgeWorldProbeOrbsPointsShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies common camera and frame values from the universal view snapshot.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.LumOnFrame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the LumOnWorldProbeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnWorldProbe { get; }
    /// <summary>Declares the VgeWorldProbeOrbsPointsParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeWorldProbeOrbsPointsParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Retains the worldProbeRadianceAtlas sampler.</summary>
    [ShaderBinding("worldProbeRadianceAtlas", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment)]
    GpuTexture? WorldProbeRadianceAtlas { set; }
    /// <summary>Retains the worldProbeVis0 sampler.</summary>
    [ShaderBinding("worldProbeVis0", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
    GpuTexture? WorldProbeVis0 { set; }
    /// <summary>Retains the worldProbeDebugState0 sampler.</summary>
    [ShaderBinding("worldProbeDebugState0", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment)]
    GpuTexture? WorldProbeDebugState0 { set; }
    #endregion
}
