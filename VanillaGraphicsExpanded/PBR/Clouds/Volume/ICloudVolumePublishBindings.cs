using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares solver reads and disjoint drawable publication writes.</summary>
internal interface ICloudVolumePublishBindings
{
    #region Public API
    /// <summary>Supplies block extents and full-copy channel masks.</summary>
    [ShaderBinding("VolumePublish",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs {get;}
    /// <summary>Reads completed stored extinction.</summary>
    [ShaderBinding("sourceDensity",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Density {set;}
    /// <summary>Reads completed direct visibility.</summary>
    [ShaderBinding("sourceDirect",ShaderBindingKind.Sampler,1,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Direct {set;}
    /// <summary>Reads completed diffuse irradiance.</summary>
    [ShaderBinding("sourceDiffuse",ShaderBindingKind.Sampler,2,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Diffuse {set;}
    /// <summary>Writes drawable extinction without touching solver storage.</summary>
    [ShaderBinding("targetDensity",ShaderBindingKind.Image,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    RHIImageBinding TargetDensity {set;}
    /// <summary>Writes drawable direct visibility.</summary>
    [ShaderBinding("targetDirect",ShaderBindingKind.Image,1,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    RHIImageBinding TargetDirect {set;}
    /// <summary>Writes drawable diffuse irradiance.</summary>
    [ShaderBinding("targetDiffuse",ShaderBindingKind.Image,2,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    RHIImageBinding TargetDiffuse {set;}
    /// <summary>Consumes dirty flags only after fenced completion.</summary>
    [ShaderBinding("PublishRegions",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Regions {set;}
    /// <summary>Reads repaired filter-footprint occupancy.</summary>
    [ShaderBinding("PublishOccupancy",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Occupancy {set;}
    /// <summary>Writes isolated drawable completion metadata.</summary>
    [ShaderBinding("PublishedRegions",ShaderBindingKind.StorageBlock,2,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? TargetRegions {set;}
    /// <summary>Writes isolated drawable occupancy.</summary>
    [ShaderBinding("PublishedOccupancy",ShaderBindingKind.StorageBlock,3,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? TargetOccupancy {set;}
    /// <summary>Reuses retired selection summaries for a bounded unique dirty-block list.</summary>
    [ShaderBinding("PublishScratch",ShaderBindingKind.StorageBlock,4,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch {set;}
    /// <summary>Produces dispatch XYZ plus the exact list count without a CPU readback.</summary>
    [ShaderBinding("PublishCommands",ShaderBindingKind.StorageBlock,5,ShaderStageKind.Compute)]
    GpuStorageBufferBinding Commands {set;}
    #endregion
}
