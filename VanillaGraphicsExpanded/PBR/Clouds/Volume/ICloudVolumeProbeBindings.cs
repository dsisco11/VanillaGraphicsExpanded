using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Samples the actual stored-volume representation through ordinary production shader bindings.</summary>
internal interface ICloudVolumeProbeBindings
{
    #region Public API
    /// <summary>Supplies one circular hardware-filter lookup coordinate and its diffuse scale.</summary>
    [ShaderBinding("VolumeProbe", ShaderBindingKind.UniformBlock, 0, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Samples the density with the texture owner's linear and mixed-wrap state.</summary>
    [ShaderBinding("probeDensity", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D, Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Density { set; }
    /// <summary>Samples the cached direct source visibility.</summary>
    [ShaderBinding("probeDirect", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D, Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Direct { set; }
    /// <summary>Samples diffuse RGB with hardware trilinear interpolation.</summary>
    [ShaderBinding("probeDiffuse", ShaderBindingKind.Sampler, 2, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D, Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Diffuse { set; }
    /// <summary>Writes two diagnostic records without whole-texture readback.</summary>
    [ShaderBinding("ProbeResult", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Result { set; }
    #endregion
}
