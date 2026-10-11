using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Declares the compute camera metering pass inputs.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface ICameraHistogramShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies metering and adaptation parameters.</summary>
    [ShaderBinding("CameraExposureInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Samples sceneRadiance without filtering histogram or history values.</summary>
    [ShaderBinding("sceneRadiance", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? SceneRadiance { set; }
    /// <summary>Receives all workgroup histograms for the dependent reduction.</summary>
    [ShaderBinding("CameraHistogram", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Histogram { set; }
    #endregion
}
