using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Exercises the actual DDA and texture sampler through ordinary typed diagnostic bindings.</summary>
internal interface ICloudVolumeTraversalProbeBindings
{
    #region Public API
    /// <summary>Supplies grid geometry and one finite test ray.</summary>
    [ShaderBinding("TraversalProbe",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs {get;}
    /// <summary>Fetches constant-cell extinction through the retained circular physical cell address.</summary>
    [ShaderBinding("volumeDensity",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTexture? Density {set;}
    /// <summary>Supplies exact conservative occupancy certificates.</summary>
    [ShaderBinding("VolumeOccupancy",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Occupancy {set;}
    /// <summary>Returns bounded interval, optical depth and completion diagnostics.</summary>
    [ShaderBinding("TraversalResult",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Result {set;}
    #endregion
}
