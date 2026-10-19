using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Declares disjoint maintenance inputs and destinations through typed production bindings.</summary>
internal interface ILightingVolumeMaintenanceBindings
{
    #region Public API
    /// <summary>Supplies dimensions, relative remap offsets and field scales.</summary>
    [ShaderBinding("LightingMaintenance", ShaderBindingKind.UniformBlock, 0, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Reads the old direct field exactly during remapping.</summary>
    [ShaderBinding("oldDirect", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D, Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? DirectInput { set; }
    /// <summary>Reads encoded diffuse RGB and numeric masks exactly during copying.</summary>
    [ShaderBinding("oldDiffuse", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D, Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? DiffuseInput { set; }
    /// <summary>Writes the separate direct destination.</summary>
    [ShaderBinding("newDirect", ShaderBindingKind.Image, 0, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding DirectOutput { set; }
    /// <summary>Writes the separate diffuse destination.</summary>
    [ShaderBinding("newDiffuse", ShaderBindingKind.Image, 1, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding DiffuseOutput { set; }
    /// <summary>Reads immutable region records for this dispatch.</summary>
    [ShaderBinding("OldRegions", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? RegionInput { set; }
    /// <summary>Writes remapped region records without overlapping old reads.</summary>
    [ShaderBinding("NewRegions", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? RegionOutput { set; }
    #endregion
}
