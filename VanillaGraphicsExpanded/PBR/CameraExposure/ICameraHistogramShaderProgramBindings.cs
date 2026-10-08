using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Declares the portable camera metering pass inputs.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface ICameraHistogramShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies metering and adaptation parameters.</summary>
    [ShaderBinding("CameraExposureInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Samples sceneRadiance without filtering histogram or history values.</summary>
    [ShaderBinding("sceneRadiance", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? SceneRadiance { set; }
    #endregion
}
