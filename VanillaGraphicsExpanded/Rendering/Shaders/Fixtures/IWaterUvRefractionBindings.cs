using System.Numerics;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Supplies optical geometry and coherent receiver images to the UV receiver fixture.</summary>
internal interface IWaterUvRefractionBindings
{
    #region Public API
    /// <summary>Reuses the optical fixture camera through the universal frame contract.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Publishes the complete retained optical input block.</summary>
    [ShaderBinding("WaterUvRefractionInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies linear radiance and eligibility metadata.</summary>
    [ShaderBinding("vge_refractionColor", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Color { get; set; }
    /// <summary>Supplies coherent hardware depth and optional source coordinates.</summary>
    [ShaderBinding("vge_refractionDepth", ShaderBindingKind.Sampler, 10, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Depth { get; set; }
    /// <summary>Supplies the view-space interface point.</summary>
    Vector3 Surface { get; set; }

    /// <summary>Supplies the oriented view-space interface normal.</summary>
    Vector3 Normal { get; set; }



    /// <summary>Selects the submerged-camera exit interface.</summary>
    int Underwater { get; set; }

    #endregion
}
