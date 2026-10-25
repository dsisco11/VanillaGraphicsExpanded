using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares coherent stored-field gather inputs and a disjoint diffuse destination.</summary>
internal interface ICloudVolumeDiffuseBindings
{
    #region Public API
    /// <summary>Supplies finite geometry, physical sources and the paired field scales.</summary>
    [ShaderBinding("VolumeDiffuse",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs {get;}
    /// <summary>Fetches authoritative constant-cell extinction by exact physical texel address.</summary>
    [ShaderBinding("volumeDensity",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Density {set;}
    /// <summary>Samples the coherent previous diffuse source, never the writable destination.</summary>
    [ShaderBinding("diffuseRead",ShaderBindingKind.Sampler,1,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Source {set;}
    /// <summary>Samples cached direct visibility only in the compiled self-shadowing variant.</summary>
    [ShaderBinding("directRead",ShaderBindingKind.Sampler,2,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Direct {set;}
    /// <summary>Reads certified filter-support occupancy.</summary>
    [ShaderBinding("VolumeOccupancy",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Occupancy {set;}
    /// <summary>Consumes selected jobs and records failed gather arithmetic.</summary>
    [ShaderBinding("DiffuseScratch",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch {set;}
    /// <summary>Writes only selected nodes in the already copied spare field.</summary>
    [ShaderBinding("diffuseWrite",ShaderBindingKind.Image,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Destination {set;}
    #endregion
}
