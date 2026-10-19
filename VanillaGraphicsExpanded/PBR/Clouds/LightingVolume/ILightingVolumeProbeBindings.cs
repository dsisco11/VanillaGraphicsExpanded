using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Supplies candidate queries through real typed bindings for isolated GPU checks.</summary>
internal interface ILightingVolumeProbeBindings
{
    #region Public API
    /// <summary>Borrows current native density descriptors.</summary>
    [ShaderBinding("candidateMap",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Uses continuous hardware-filtered direct lighting.</summary>
    [ShaderBinding("candidateDirect",ShaderBindingKind.Sampler,1,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? Direct { set; }
    /// <summary>Uses continuous hardware-filtered RGB and exact alpha fetches.</summary>
    [ShaderBinding("candidateDiffuse",ShaderBindingKind.Sampler,2,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? Diffuse { set; }
    /// <summary>Supplies native/grid placement and physical storage units.</summary>
    [ShaderBinding("LightingVolumeInputs",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies one bounded native-local query point.</summary>
    [ShaderBinding("LightingVolumeQuery",ShaderBindingKind.UniformBlock,1,ShaderStageKind.Compute)]
    CpuUniformBuffer Query { get; }
    /// <summary>Stores density, transmission and diffuse diagnostic values.</summary>
    [ShaderBinding("LightingVolumeResult",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Result { set; }
    #endregion
}
