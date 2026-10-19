using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Declares disjoint published-source reads, spare writes and GPU-owned refinement jobs.</summary>
internal interface ILightingVolumeScatteringBindings
{
    #region Public API
    /// <summary>Publishes native placement and the readable field's decode scale.</summary>
    [ShaderBinding("LightingVolumeInputs",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Publishes physical sources, destination scale and region alignment.</summary>
    [ShaderBinding("LightingScatteringInputs",ShaderBindingKind.UniformBlock,1,ShaderStageKind.Compute)]
    CpuUniformBuffer Sources { get; }
    /// <summary>Borrows the exact current native density map.</summary>
    [ShaderBinding("candidateMap",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Reads only completed direct transmission with hardware filtering.</summary>
    [ShaderBinding("candidateDirect",ShaderBindingKind.Sampler,1,ShaderStageKind.Compute,Sampler=ShaderSamplerPolicy.LinearClamp,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTexture? Direct { set; }
    /// <summary>Reads the immutable published source field and its exact alpha masks.</summary>
    [ShaderBinding("candidateDiffuse",ShaderBindingKind.Sampler,2,ShaderStageKind.Compute,Sampler=ShaderSamplerPolicy.LinearClamp,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTexture? Previous { set; }
    /// <summary>Writes selected nodes into the already copied spare field.</summary>
    [ShaderBinding("scatteringTarget",ShaderBindingKind.Image,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Target { set; }
    /// <summary>Reads selection and records region-wide arithmetic failure.</summary>
    [ShaderBinding("ScatteringRegions",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Regions { set; }
    /// <summary>Reads the bounded diffuse job list and count.</summary>
    [ShaderBinding("ScatteringJobs",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Jobs { set; }
    #endregion
}
