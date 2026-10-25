using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares stored-density direct transport with no native weather or old-renderer dependency.</summary>
internal interface ICloudVolumeDirectBindings
{
    #region Public API
    /// <summary>Supplies finite geometry and the frozen source direction.</summary>
    [ShaderBinding("VolumeDirect",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs {get;}
    /// <summary>Fetches authoritative constant-cell extinction by exact physical texel address.</summary>
    [ShaderBinding("volumeDensity",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Density {set;}
    /// <summary>Reads certified filtered support.</summary>
    [ShaderBinding("VolumeOccupancy",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Occupancy {set;}
    /// <summary>Updates an ordered source channel while preserving its companion channel.</summary>
    [ShaderBinding("directOutput",ShaderBindingKind.Image,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Direct {set;}
    /// <summary>Reads bounded selected jobs and reports failed transport without stamping completion.</summary>
    [ShaderBinding("DirectScratch",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch {set;}
    #endregion
}
