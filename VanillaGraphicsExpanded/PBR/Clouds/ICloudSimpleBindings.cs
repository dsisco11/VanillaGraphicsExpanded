using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares Simple cloud resources independently of patched native shaders.</summary>
[ShaderBindingSet(typeof(CameraExposure.IPhotometricFrameBindings), Defaults = true)]
internal interface ICloudSimpleBindings : CameraExposure.IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Supplies the shared camera snapshot to geometry and depth handling.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies the native placement and owned lighting/medium parameters.</summary>
    [ShaderBinding("CloudSimpleParameters", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Borrows native liquid depth using normalized full-view coordinates.</summary>
    [ShaderBinding("liquidDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? LiquidDepth { set; }
    /// <summary>Borrows the completed aerial radiance bands with their published encoding.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D)]
    DynamicTexture3D? AerialRadiance { set; }
    /// <summary>Borrows dimensionless aerial loss from the same atmosphere publication.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D)]
    DynamicTexture3D? AerialAttenuation { set; }
    /// <summary>Uses existing shafts only for aerial in-scattering.</summary>
    [ShaderBinding("vge_lightShaftOcclusion", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.LinearClamp, Required = false)]
    GpuTexture? LightShafts { set; }
    #endregion
}
