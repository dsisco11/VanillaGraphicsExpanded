using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares exact queued work and GPU-produced dispatch counts without borrowing native resources.</summary>
internal interface ICloudVolumeWorkBindings
{
    #region Public API
    /// <summary>Supplies frozen source changes, region geometry and current visibility planes.</summary>
    [ShaderBinding("VolumeWork",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Retains target/completed revisions and first-pending ages.</summary>
    [ShaderBinding("WorkRegions",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Regions { set; }
    /// <summary>Stores bounded lists, fair cursors and completion status in existing scratch.</summary>
    [ShaderBinding("WorkScratch",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch { set; }
    /// <summary>Writes separate direct/diffuse indirect dispatch records.</summary>
    [ShaderBinding("WorkCommands",ShaderBindingKind.StorageBlock,2,ShaderStageKind.Compute)]
    GpuStorageBufferBinding Commands { set; }
    /// <summary>Excludes blocks with no stored density or trilinear support from transport queues.</summary>
    [ShaderBinding("WorkOccupancy",ShaderBindingKind.StorageBlock,3,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Occupancy { set; }
    #endregion
}
