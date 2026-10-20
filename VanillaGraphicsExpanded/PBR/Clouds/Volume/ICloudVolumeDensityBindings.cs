using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares borrowed observation inputs and candidate-owned generation/occupancy resources.</summary>
internal interface ICloudVolumeDensityBindings
{
    #region Public API
    /// <summary>Supplies bounded material offsets, window identity and work stamps.</summary>
    [ShaderBinding("VolumeDensity",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Borrows only the currently certified native map for validation and snapshot observation.</summary>
    [ShaderBinding("nativeMap",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Owns the single persistent circular descriptor snapshot.</summary>
    [ShaderBinding("descriptors",ShaderBindingKind.Image,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture2D)]
    GpuTextureBinding Descriptors { set; }
    /// <summary>Writes cell-average extinction only in selected complete physical blocks.</summary>
    [ShaderBinding("densityOutput",ShaderBindingKind.Image,1,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Density { set; }
    /// <summary>Reads stored density exactly while repairing conservative filter-footprint occupancy.</summary>
    [ShaderBinding("storedDensity",ShaderBindingKind.Sampler,1,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTexture? StoredDensity { set; }
    /// <summary>Retains demand ages, target revisions and actual completion stamps.</summary>
    [ShaderBinding("DensityRegions",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Regions { set; }
    /// <summary>Repairs occupancy before exposing new density.</summary>
    [ShaderBinding("DensityOccupancy",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Occupancy { set; }
    /// <summary>Stores bounded job lists and selection scratch inside the existing fixed allowance.</summary>
    [ShaderBinding("DensityScratch",ShaderBindingKind.StorageBlock,2,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch { set; }
    /// <summary>Returns validation bounds and completion status, never the native descriptor array.</summary>
    [ShaderBinding("DensitySummary",ShaderBindingKind.StorageBlock,3,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Summary { set; }
    /// <summary>Publishes GPU-selected dispatch counts without CPU job-list readback.</summary>
    [ShaderBinding("DensityCommands",ShaderBindingKind.StorageBlock,4,ShaderStageKind.Compute)]
    GpuStorageBufferBinding Commands { set; }
    #endregion
}
