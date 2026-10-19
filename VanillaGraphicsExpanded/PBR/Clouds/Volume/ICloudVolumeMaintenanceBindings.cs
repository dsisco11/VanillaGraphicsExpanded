using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares ordered in-place initialization and circular-window maintenance resources.</summary>
internal interface ICloudVolumeMaintenanceBindings
{
    #region Public API
    /// <summary>Supplies bounded slots, offsets, work ranges and initialized source values.</summary>
    [ShaderBinding("VolumeMaintenance", ShaderBindingKind.UniformBlock, 0, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Resets only newly admitted or guard density texels.</summary>
    [ShaderBinding("densityField", ShaderBindingKind.Image, 0, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Density { set; }
    /// <summary>Initializes direct visibility without a relocation spare.</summary>
    [ShaderBinding("directField", ShaderBindingKind.Image, 1, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Direct { set; }
    /// <summary>Maintains the published diffuse source outside sampling passes.</summary>
    [ShaderBinding("diffuseField", ShaderBindingKind.Image, 2, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Diffuse { set; }
    /// <summary>Initializes corresponding spare values for the admitted window.</summary>
    [ShaderBinding("diffuseSpare", ShaderBindingKind.Image, 3, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Spare { set; }
    /// <summary>Revokes completion stamps only on physical blocks touched by changed slots.</summary>
    [ShaderBinding("Regions", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Regions { set; }
    /// <summary>Initializes empty occupancy and conservatively marks changed blocks.</summary>
    [ShaderBinding("Occupancy", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Occupancy { set; }
    #endregion
}
