using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Declares frozen native density, selected jobs and ordered direct/mask image writes.</summary>
internal interface ILightingVolumeDirectBindings
{
    #region Public API
    /// <summary>Publishes native-local grid placement and physical density units.</summary>
    [ShaderBinding("LightingVolumeInputs",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Publishes one source direction, channel and absolute-region alignment.</summary>
    [ShaderBinding("LightingDirectInputs",ShaderBindingKind.UniformBlock,1,ShaderStageKind.Compute)]
    CpuUniformBuffer Source { get; }
    /// <summary>Borrows only the current certified native map.</summary>
    [ShaderBinding("candidateMap",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Preserves the other source's transmission through ordered image load/store.</summary>
    [ShaderBinding("directTarget",ShaderBindingKind.Image,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Direct { set; }
    /// <summary>Preserves encoded diffuse RGB and unrelated numeric mask bits.</summary>
    [ShaderBinding("directNodes",ShaderBindingKind.Image,1,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Nodes { set; }
    /// <summary>Reads selected-region identity and records per-channel arithmetic failure.</summary>
    [ShaderBinding("DirectRegions",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Regions { set; }
    /// <summary>Reads bounded selected-job counts and indices without CPU readback.</summary>
    [ShaderBinding("DirectJobs",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Jobs { set; }
    #endregion
}
